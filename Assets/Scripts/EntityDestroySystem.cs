using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace TMG.Survivors
{
    public struct EntityDestroyFlag : IComponentData, IEnableableComponent
    {
    }


    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
    public partial struct EntityDestroySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var endEcbSystem = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var endEcb = endEcbSystem.CreateCommandBuffer(state.WorldUnmanaged);
            var beginEcbSystem = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
            var beginEcb = beginEcbSystem.CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (_, entity) in SystemAPI.Query<EntityDestroyFlag>().WithEntityAccess())
            {
                if (SystemAPI.HasComponent<PlayerTag>(entity))
                {
                    GameUIController.Instance.ShowGameOverUI();
                }

                if (SystemAPI.HasComponent<GemPrefab>(entity))
                {
                    var gemPrefab = SystemAPI.GetComponent<GemPrefab>(entity).value;
                    var gemEntity = beginEcb.Instantiate(gemPrefab);

                    var spawnPos = SystemAPI.GetComponent<LocalTransform>(entity).Position;
                    beginEcb.SetComponent(gemEntity, LocalTransform.FromPosition(spawnPos));
                }

                // Debug.Log($"destroy sys: frame:{Time.frameCount}");
                endEcb.DestroyEntity(entity);
            }
        }
    }
}