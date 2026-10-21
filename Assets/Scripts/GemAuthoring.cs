using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;

namespace TMG.Survivors
{
    public struct GemTag : IComponentData
    {
    }

    public class GemAuthoring : MonoBehaviour
    {
        private class Baker : Baker<GemAuthoring>
        {
            public override void Bake(GemAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent<GemTag>(entity);
                AddComponent<EntityDestroyFlag>(entity);
                SetComponentEnabled<EntityDestroyFlag>(entity, false);
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CollectionGemSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new CollectionGemJob()
            {
                gemTagLookup = SystemAPI.GetComponentLookup<GemTag>(),
                entityDestroyFlagLookup = SystemAPI.GetComponentLookup<EntityDestroyFlag>(),
                collectionGemCountLookup = SystemAPI.GetComponentLookup<CollectionGemCount>(),
                updateGemUiFlagLookup = SystemAPI.GetComponentLookup<UpdateGemUiFlag>(),
                
                frameCount = Time.frameCount,
            };
            
            state.Dependency = job.Schedule(SystemAPI.GetSingleton<SimulationSingleton>(), state.Dependency);
        }
    }

    [BurstCompile]
    public struct CollectionGemJob : ITriggerEventsJob
    {
        [ReadOnly] public ComponentLookup<GemTag> gemTagLookup;
        public ComponentLookup<EntityDestroyFlag> entityDestroyFlagLookup;
        public ComponentLookup<CollectionGemCount> collectionGemCountLookup;
        public ComponentLookup<UpdateGemUiFlag> updateGemUiFlagLookup;

        [ReadOnly] public int frameCount;
        
        public void Execute(TriggerEvent triggerEvent)
        {
            Entity gemEntity;
            Entity playerEntity;

            if (gemTagLookup.HasComponent(triggerEvent.EntityA) && collectionGemCountLookup.HasComponent(triggerEvent.EntityB))
            {
                gemEntity = triggerEvent.EntityA;
                playerEntity = triggerEvent.EntityB;
            }
            else if (gemTagLookup.HasComponent(triggerEvent.EntityB) && collectionGemCountLookup.HasComponent(triggerEvent.EntityA))
            {
                gemEntity = triggerEvent.EntityB;
                playerEntity = triggerEvent.EntityA;
            }
            else
            {
                return;
            }
            
            // Debug.Log($"count:{frameCount}" );
            // gem-count +1
            if (entityDestroyFlagLookup.IsComponentEnabled(gemEntity) == false)
            {
                // 过滤已经标记为销毁的gem
                // 下一帧 模拟层的时候，gem-entity还未被真正销毁，还能检测到碰撞
                // ecb 销毁在当前操作帧的下一帧的模拟层结束后，表现层开始前
                var gemCountComponent = collectionGemCountLookup[playerEntity];
                gemCountComponent.value += 1;
                collectionGemCountLookup[playerEntity] = gemCountComponent;
                
                // 销毁 gem-entity
                entityDestroyFlagLookup.SetComponentEnabled(gemEntity, true);
                
                // update gem ui
                updateGemUiFlagLookup.SetComponentEnabled(playerEntity, true);
            }
        }
    }
}