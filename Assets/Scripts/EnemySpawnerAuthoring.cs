using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace TMG.Survivors
{
    public struct SpawnEnemyData : IComponentData
    {
        public Entity enemyPrefab;
        public float spawnInterval;
        public float spawnDistance;
    }

    public struct EnemySpawnState : IComponentData
    {
        public float spawnTimer;
        public Random random;
    }

    public class EnemySpawnerAuthoring : MonoBehaviour
    {
        public GameObject enemyPrefab;
        public float spawnInterval;
        public float spawnDistance;
        public uint randomSeed;

        private class Baker : Baker<EnemySpawnerAuthoring>
        {
            public override void Bake(EnemySpawnerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new SpawnEnemyData()
                {
                    enemyPrefab = GetEntity(authoring.enemyPrefab, TransformUsageFlags.Dynamic),
                    spawnInterval = authoring.spawnInterval,
                    spawnDistance = authoring.spawnDistance,
                });
                AddComponent(entity, new EnemySpawnState()
                {
                    spawnTimer = 0f,
                    random = Random.CreateFromIndex(authoring.randomSeed),
                });
            }
        }
    }

    public partial struct EnemySpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var ecbSystem = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);

            var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
            var playerPos = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;
            foreach (var (spawnState, spawnEnemyData)
                     in SystemAPI.Query<RefRW<EnemySpawnState>, SpawnEnemyData>())
            {
                spawnState.ValueRW.spawnTimer -= deltaTime;
                if (spawnState.ValueRW.spawnTimer > 0f)
                {
                    continue;
                }

                spawnState.ValueRW.spawnTimer = spawnEnemyData.spawnInterval;

                var newEnemy = ecb.Instantiate(spawnEnemyData.enemyPrefab);
                var spawnAngle = spawnState.ValueRW.random.NextFloat(0, math.TAU);
                var spawnPos = new float3
                {
                    x = math.sin(spawnAngle),
                    y = math.cos(spawnAngle),
                    z = 0f,
                };
                spawnPos *= spawnEnemyData.spawnDistance;
                spawnPos += playerPos;

                ecb.SetComponent(newEnemy, LocalTransform.FromPosition(spawnPos));
            }
        }
    }
}