using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

namespace TMG.Survivors
{
    public struct PlasmaBlastData : IComponentData
    {
        public float moveSpeed;
        public int attackDamage;
    }

    public class PlasmaBlastAuthoring : MonoBehaviour
    {
        public float moveSpeed;
        public int attackDamage;

        private class Beker : Baker<PlasmaBlastAuthoring>
        {
            public override void Bake(PlasmaBlastAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new PlasmaBlastData()
                {
                    moveSpeed = authoring.moveSpeed,
                    attackDamage = authoring.attackDamage,
                });
                
                AddComponent<EntityDestroyFlag>(entity);
                SetComponentEnabled<EntityDestroyFlag>(entity, false);
            }
        }
    }

    public partial struct MovePlasmaBlastSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var (localTransform, plasmaBlastData)
                     in SystemAPI.Query<RefRW<LocalTransform>, PlasmaBlastData>())
            {
                localTransform.ValueRW.Position += localTransform.ValueRO.Right() * plasmaBlastData.moveSpeed * deltaTime;
            }
        }
    }

    [UpdateInGroup(typeof(PhysicsSystemGroup))]
    [UpdateAfter(typeof(PhysicsSimulationGroup))]
    [UpdateBefore(typeof(AfterPhysicsSystemGroup))]
    public partial struct PlasmaBlastAttackSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var attackJob = new PlasmaBlastAttackJob()
            {
                plasmaBlastDataLookup = SystemAPI.GetComponentLookup<PlasmaBlastData>(),
                enemyLookup = SystemAPI.GetComponentLookup<EnemyTag>(),
                damageThisFrameLookup = SystemAPI.GetBufferLookup<DamageThisFrame>(),
                destroyTagLookup = SystemAPI.GetComponentLookup<EntityDestroyFlag>(),
            };

            state.Dependency = attackJob.Schedule(SystemAPI.GetSingleton<SimulationSingleton>(), state.Dependency);
        }
    }

    public struct PlasmaBlastAttackJob : ITriggerEventsJob
    {
        [ReadOnly] public ComponentLookup<PlasmaBlastData> plasmaBlastDataLookup;
        [ReadOnly] public ComponentLookup<EnemyTag> enemyLookup;
        public BufferLookup<DamageThisFrame> damageThisFrameLookup;
        public ComponentLookup<EntityDestroyFlag> destroyTagLookup;

        public void Execute(TriggerEvent triggerEvent)
        {
            Entity plasmaBlastEntity;
            Entity enemyEntity;

            if (plasmaBlastDataLookup.HasComponent(triggerEvent.EntityA) && enemyLookup.HasComponent(triggerEvent.EntityB))
            {
                plasmaBlastEntity = triggerEvent.EntityA;
                enemyEntity = triggerEvent.EntityB;
            }
            else if (plasmaBlastDataLookup.HasComponent(triggerEvent.EntityB) && enemyLookup.HasComponent(triggerEvent.EntityA))
            {
                plasmaBlastEntity = triggerEvent.EntityB;
                enemyEntity = triggerEvent.EntityA;
            }
            else
            {
                return;
            }

            var attackDamage = plasmaBlastDataLookup[plasmaBlastEntity].attackDamage;
            var enemyDamageBuffer = damageThisFrameLookup[enemyEntity];
            enemyDamageBuffer.Add(new DamageThisFrame()
            {
                value = attackDamage,
            });
            
            destroyTagLookup.SetComponentEnabled(plasmaBlastEntity, true);
        }
    }
}