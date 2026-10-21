using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace TMG.Survivors
{
    public struct EnemyTag : IComponentData
    {
    }

    public struct EnemyAttackData : IComponentData
    {
        public int damageHp;
        public float cooldown;
    }

    public struct EnemyCooldownExpirationTimestamp : IComponentData, IEnableableComponent
    {
        public double value;
    }

    public struct GemPrefab : IComponentData
    {
        public Entity value;
    }

    [RequireComponent(typeof(CharacterAuthoring))]
    public class EnemyAuthoring : MonoBehaviour
    {
        public int damageHp;
        public float cooldown;
        public GameObject gemPrefab;

        private class Baker : Baker<EnemyAuthoring>
        {
            public override void Bake(EnemyAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<EnemyTag>(entity);
                AddComponent(entity, new EnemyAttackData()
                {
                    damageHp = authoring.damageHp,
                    cooldown = authoring.cooldown,
                });
                AddComponent<EnemyCooldownExpirationTimestamp>(entity);
                SetComponentEnabled<EnemyCooldownExpirationTimestamp>(entity, false);
                AddComponent(entity, new GemPrefab()
                {
                    value = GetEntity(authoring.gemPrefab, TransformUsageFlags.Dynamic),
                });
            }
        }
    }

    public partial struct EnemyMovetoPlayerSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
            var playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position.xy;

            var moveToPlayerJob = new EnemyMoveToPlayerJob()
            {
                playerPosition = playerPosition,
            };

            state.Dependency = moveToPlayerJob.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    [WithAll(typeof(EnemyTag))]
    internal partial struct EnemyMoveToPlayerJob : IJobEntity
    {
        public float2 playerPosition;

        private void Execute(ref CharacterMoveDirection direction, in LocalTransform localTransform)
        {
            var vectorToPlayer = playerPosition - localTransform.Position.xy;
            direction.value = math.normalize(vectorToPlayer);
        }
    }

    [UpdateInGroup(typeof(PhysicsSystemGroup))]
    [UpdateAfter(typeof(PhysicsSimulationGroup))]
    [UpdateBefore(typeof(AfterPhysicsSystemGroup))]
    public partial struct EnemyAttackSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var elapsedTime = SystemAPI.Time.ElapsedTime;
            foreach (var (cooldownExpirationTimestamp, enableCooldown)
                     in SystemAPI.Query<EnemyCooldownExpirationTimestamp, EnabledRefRW<EnemyCooldownExpirationTimestamp>>())
            {
                if (cooldownExpirationTimestamp.value > elapsedTime)
                {
                    continue;
                }

                enableCooldown.ValueRW = false;
            }

            var enemyAttackJob = new EnemyAttackJob()
            {
                playerLookup = SystemAPI.GetComponentLookup<PlayerTag>(true),
                enemyAttackDataLookup = SystemAPI.GetComponentLookup<EnemyAttackData>(true),
                enemyCdTimestampLookup = SystemAPI.GetComponentLookup<EnemyCooldownExpirationTimestamp>(),
                damageThisFrameLookup = SystemAPI.GetBufferLookup<DamageThisFrame>(),
                elapsedTime = elapsedTime,
            };

            var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
            state.Dependency = enemyAttackJob.Schedule(simulationSingleton, state.Dependency);
        }
    }

    [BurstCompile]
    public struct EnemyAttackJob : ICollisionEventsJob
    {
        [ReadOnly] public ComponentLookup<PlayerTag> playerLookup;
        [ReadOnly] public ComponentLookup<EnemyAttackData> enemyAttackDataLookup;
        public ComponentLookup<EnemyCooldownExpirationTimestamp> enemyCdTimestampLookup;
        public BufferLookup<DamageThisFrame> damageThisFrameLookup;

        public double elapsedTime;

        public void Execute(CollisionEvent collisionEvent)
        {
            Entity playerEntity;
            Entity enemyEntity;

            if (playerLookup.HasComponent(collisionEvent.EntityA) && enemyAttackDataLookup.HasComponent(collisionEvent.EntityB))
            {
                playerEntity = collisionEvent.EntityA;
                enemyEntity = collisionEvent.EntityB;
            }
            else if (playerLookup.HasComponent(collisionEvent.EntityB) && enemyAttackDataLookup.HasComponent(collisionEvent.EntityA))
            {
                playerEntity = collisionEvent.EntityB;
                enemyEntity = collisionEvent.EntityA;
            }
            else
            {
                return;
            }

            if (enemyCdTimestampLookup.IsComponentEnabled(enemyEntity))
            {
                return;
            }

            var attackData = enemyAttackDataLookup[enemyEntity];
            enemyCdTimestampLookup[enemyEntity] = new EnemyCooldownExpirationTimestamp()
            {
                value = elapsedTime + attackData.cooldown,
            };
            enemyCdTimestampLookup.SetComponentEnabled(enemyEntity, true);

            var damageBuffer = damageThisFrameLookup[playerEntity];
            damageBuffer.Add(new DamageThisFrame()
            {
                value = attackData.damageHp,
            });
        }
    }
}