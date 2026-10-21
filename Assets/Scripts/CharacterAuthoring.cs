using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using UnityEngine;

namespace TMG.Survivors
{
    public struct CharacterInitFlag : IComponentData, IEnableableComponent
    {
    }

    public struct CharacterMoveDirection : IComponentData
    {
        public float2 value;
    }

    public struct CharacterMoveSpeed : IComponentData
    {
        public float value;
    }

    [MaterialProperty("_FacingDirection")]
    public struct FacingDirection : IComponentData
    {
        public float value;
    }

    public struct CharacterMaxHp : IComponentData
    {
        public int value;
    }

    public struct CharacterCurHp : IComponentData
    {
        public int value;
    }

    public struct DamageThisFrame : IBufferElementData
    {
        public int value;
    }

    public class CharacterAuthoring : MonoBehaviour
    {
        public float moveSpeed;
        public int maxHp;

        private class Baker : Baker<CharacterAuthoring>
        {
            public override void Bake(CharacterAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<CharacterInitFlag>(entity);
                AddComponent<CharacterMoveDirection>(entity);
                AddComponent(entity, new CharacterMoveSpeed()
                {
                    value = authoring.moveSpeed
                });
                AddComponent(entity, new FacingDirection()
                {
                    value = 1
                });
                AddComponent(entity, new CharacterMaxHp()
                {
                    value = authoring.maxHp,
                });
                AddComponent(entity, new CharacterCurHp()
                {
                    value = authoring.maxHp,
                });
                AddBuffer<DamageThisFrame>(entity);
                AddComponent<EntityDestroyFlag>(entity);
                SetComponentEnabled<EntityDestroyFlag>(entity, false);
            }
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct CharacterInitSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (physicsMass, initFlag)
                     in SystemAPI.Query<RefRW<PhysicsMass>, EnabledRefRW<CharacterInitFlag>>())
            {
                physicsMass.ValueRW.InverseInertia = float3.zero;
                initFlag.ValueRW = false;
            }
        }
    }

    public partial struct CharacterMoveSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (velocity, facingDirection, direction, moveSpeed, entity)
                     in SystemAPI.Query<RefRW<PhysicsVelocity>, RefRW<FacingDirection>, CharacterMoveDirection, CharacterMoveSpeed>()
                                 .WithEntityAccess())
            {
                var moveStep2D = direction.value * moveSpeed.value;
                velocity.ValueRW.Linear += new float3(moveStep2D, 0f);

                if (math.abs(direction.value.x) > 0.15f)
                {
                    facingDirection.ValueRW.value = direction.value.x > 0 ? 1 : -1;
                }

                if (SystemAPI.HasComponent<PlayerTag>(entity))
                {
                    var animation = math.lengthsq(moveStep2D) > float.Epsilon ? PlayerAnimationIndex.Move : PlayerAnimationIndex.Idle;

                    var animationIndex = SystemAPI.GetComponentRW<AnimationIndexOverride>(entity);
                    animationIndex.ValueRW.value = (float)animation;
                }
            }
        }
    }

    public partial struct GlobalTimeUpdateSystem : ISystem
    {
        private static int globalTimeShaderPropertyID;

        public void OnCreate(ref SystemState state)
        {
            globalTimeShaderPropertyID = Shader.PropertyToID("_GlobalTime");
        }

        public void OnUpdate(ref SystemState state)
        {
            Shader.SetGlobalFloat(globalTimeShaderPropertyID, (float)SystemAPI.Time.ElapsedTime);
        }
    }

    public partial struct ProcessDamageThisFrameSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (curHp, damageThisFrames, entity)
                     in SystemAPI.Query<RefRW<CharacterCurHp>, DynamicBuffer<DamageThisFrame>>().WithPresent<EntityDestroyFlag>().WithEntityAccess())
            {
                if (damageThisFrames.IsEmpty)
                {
                    continue;
                }

                foreach (var damageThisFrame in damageThisFrames)
                {
                    curHp.ValueRW.value -= damageThisFrame.value;
                }

                damageThisFrames.Clear();

                if (curHp.ValueRO.value <= 0)
                {
                    SystemAPI.SetComponentEnabled<EntityDestroyFlag>(entity, true);
                }
            }
        }
    }
}