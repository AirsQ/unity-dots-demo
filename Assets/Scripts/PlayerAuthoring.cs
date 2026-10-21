using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;

namespace TMG.Survivors
{
    public struct PlayerTag : IComponentData
    {
    }

    public struct CameraTarget : IComponentData
    {
        public UnityObjectRef<Transform> cameraTransform;
    }

    public struct InitCameraTargetTag : IComponentData
    {
    }

    [MaterialProperty("_AnimationIndex")]
    public struct AnimationIndexOverride : IComponentData
    {
        public float value;
    }

    public enum PlayerAnimationIndex : byte
    {
        Move = 0,
        Idle = 1,

        None = byte.MaxValue,
    }

    public struct PlayerAttackData : IComponentData
    {
        public Entity attackPrefab;
        public double cooldown;
        public float3 detectionSize;
        public CollisionFilter collisionFilter;
    }

    public struct PlayerCooldownTimer : IComponentData, IEnableableComponent
    {
        public double value;
    }

    public struct CollectionGemCount : IComponentData
    {
        public int value;
    }

    public struct UpdateGemUiFlag : IComponentData, IEnableableComponent
    {
    }

    public struct PlayerHpWorldUI : IComponentData
    {
        public UnityObjectRef<Transform> canvasTransform;
        public UnityObjectRef<Slider> hpBarSlider;
    }

    public struct PlayerHpWorldUIPrefab : IComponentData
    {
        public UnityObjectRef<GameObject> value;
    }

    public class PlayerAuthoring : MonoBehaviour
    {
        public GameObject playerAttackPrefab;
        public double cooldown;
        public float detectionSize;
        public GameObject playerHpWorldUIPrefab;

        private class Baker : Baker<PlayerAuthoring>
        {
            public override void Bake(PlayerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<PlayerTag>(entity);
                AddComponent<InitCameraTargetTag>(entity);
                AddComponent<CameraTarget>(entity);
                AddComponent<AnimationIndexOverride>(entity);

                var enemyLay = LayerMask.NameToLayer("Enemy");
                var enemyLayerMask = (uint)math.pow(2, enemyLay);
                var attackCollisionFilter = new CollisionFilter()
                {
                    BelongsTo = uint.MaxValue,
                    CollidesWith = enemyLayerMask,
                };

                AddComponent(entity, new PlayerAttackData()
                {
                    attackPrefab = GetEntity(authoring.playerAttackPrefab, TransformUsageFlags.Dynamic),
                    cooldown = authoring.cooldown,
                    detectionSize = authoring.detectionSize,
                    collisionFilter = attackCollisionFilter,
                });
                AddComponent<PlayerCooldownTimer>(entity);
                // SetComponentEnabled<PlayerCooldownTimer>(entity, false);

                AddComponent<CollectionGemCount>(entity);

                AddComponent<UpdateGemUiFlag>(entity);
                SetComponentEnabled<UpdateGemUiFlag>(entity, false);

                AddComponent(entity, new PlayerHpWorldUIPrefab()
                {
                    value = authoring.playerHpWorldUIPrefab,
                });
            }
        }
    }

    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial struct MoveCameraSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (transform, cameraTarget) in SystemAPI.Query<LocalToWorld, CameraTarget>().WithAll<PlayerTag>().WithNone<InitCameraTargetTag>())
            {
                cameraTarget.cameraTransform.Value.position = transform.Position;
            }
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct CameraInitSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InitCameraTargetTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (CameraTargetSingleton.instance == null)
            {
                return;
            }

            var cameraTargetTransform = CameraTargetSingleton.instance.transform;
            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
            foreach (var (cameraTarget, entity)
                     in SystemAPI.Query<RefRW<CameraTarget>>().WithAll<InitCameraTargetTag, PlayerTag>().WithEntityAccess())
            {
                cameraTarget.ValueRW.cameraTransform = cameraTargetTransform;
                ecb.RemoveComponent<InitCameraTargetTag>(entity);
            }

            ecb.Playback(state.EntityManager);
        }
    }

    public partial class PlayerMoveDirectionSystem : SystemBase
    {
        private Survivorsinput input;

        protected override void OnCreate()
        {
            input = new Survivorsinput();
            input.Enable();
        }

        protected override void OnUpdate()
        {
            float2 inputDirection = input.Player.Move.ReadValue<Vector2>();
            foreach (var moveDiDirection in SystemAPI.Query<RefRW<CharacterMoveDirection>>().WithAll<PlayerTag>())
            {
                moveDiDirection.ValueRW.value = inputDirection;
            }
        }
    }

    public partial struct PlayerAttackSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var elapsedTime = SystemAPI.Time.ElapsedTime;

            var ecbSystem = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);

            var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

            foreach (var (playerCooldownTimer, playerAttackData, localTransform)
                     in SystemAPI.Query<RefRW<PlayerCooldownTimer>, PlayerAttackData, LocalTransform>())
            {
                if (playerCooldownTimer.ValueRO.value > elapsedTime)
                {
                    continue;
                }

                var spawnPos = localTransform.Position;
                var minDetectionPos = spawnPos - playerAttackData.detectionSize;
                var maxDetectionPos = spawnPos + playerAttackData.detectionSize;
                var aabbInput = new OverlapAabbInput()
                {
                    Aabb = new Aabb()
                    {
                        Min = minDetectionPos,
                        Max = maxDetectionPos,
                    },
                    Filter = playerAttackData.collisionFilter,
                };

                var overlapHits = new NativeList<int>(state.WorldUpdateAllocator);
                if (!physicsWorldSingleton.OverlapAabb(aabbInput, ref overlapHits))
                {
                    continue;
                }

                var maxDistanceSq = float.MaxValue;
                var closestEnemyPos = float3.zero;
                foreach (var hit in overlapHits)
                {
                    var curEnemyPos = physicsWorldSingleton.Bodies[hit].WorldFromBody.pos;
                    var distanceSq = math.distancesq(spawnPos.xy, curEnemyPos.xy);
                    if (distanceSq < maxDistanceSq)
                    {
                        maxDistanceSq = distanceSq;
                        closestEnemyPos = curEnemyPos;
                    }
                }

                var vectorToClosestEnemy = closestEnemyPos - spawnPos;
                var angeleToClosestEnemy = math.atan2(vectorToClosestEnemy.y, vectorToClosestEnemy.x);
                var spawnOrientation = quaternion.Euler(0f, 0f, angeleToClosestEnemy);

                var newAttack = ecb.Instantiate(playerAttackData.attackPrefab);
                ecb.SetComponent(newAttack, LocalTransform.FromPositionRotation(spawnPos, spawnOrientation));

                playerCooldownTimer.ValueRW.value = elapsedTime + playerAttackData.cooldown;
            }
        }
    }

    public partial struct UpdateGemUiSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (collectionGemCount, updateFlag)
                     in SystemAPI.Query<CollectionGemCount, EnabledRefRW<UpdateGemUiFlag>>())
            {
                GameUIController.Instance.UpdateGemsCollectedText(collectionGemCount.value);
                updateFlag.ValueRW = false;
            }
        }
    }

    public partial struct PlayerWorldUISystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

            // 创建 hp-bar
            foreach (var (playerHpWorldUIPrefab, entity)
                     in SystemAPI.Query<PlayerHpWorldUIPrefab>().WithNone<PlayerHpWorldUI>().WithEntityAccess())
            {
                var hpUI = Object.Instantiate(playerHpWorldUIPrefab.value.Value);

                ecb.AddComponent(entity, new PlayerHpWorldUI()
                {
                    canvasTransform = hpUI.transform,
                    hpBarSlider = hpUI.GetComponentInChildren<Slider>(),
                });
            }

            // 更新 hp-bar
            foreach (var (transform, hpUI, curHp, maxHp)
                     in SystemAPI.Query<LocalToWorld, PlayerHpWorldUI, CharacterCurHp, CharacterMaxHp>())
            {
                hpUI.canvasTransform.Value.position = transform.Position;
                var hpPercent = (float)curHp.value / maxHp.value;
                hpUI.hpBarSlider.Value.value = hpPercent;
            }

            // clean hp-bar
            foreach (var (hpUI, entity)
                     in SystemAPI.Query<PlayerHpWorldUI>().WithAll<EntityDestroyFlag>().WithEntityAccess())
            {
                if (hpUI.canvasTransform.Value != null)
                {
                    Object.Destroy(hpUI.canvasTransform.Value.gameObject);
                }

                ecb.RemoveComponent<PlayerHpWorldUI>(entity);
            }

            ecb.Playback(state.EntityManager);
        }
    }
}