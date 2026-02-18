using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using UnityEngine;

namespace TheraplyExamples
{
    /// <summary>
    /// Registers example games in scenes that host GameRegistryService.
    /// Keeps game-specific IDs out of TheraplyCore runtime layer.
    /// </summary>
    public static class ExampleGameRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterExampleGames()
        {
            var registry = Object.FindFirstObjectByType<GameRegistryService>();
            if (registry == null)
            {
                return;
            }

            EnsureRegistered<SmokeTestGameModule>(
                registry,
                SmokeTestGameConfig.DefaultGameId,
                "SmokeTestGameRuntime");

            EnsureRegistered<DemoCubeGameModule>(
                registry,
                DemoCubeGameConfig.DefaultGameId,
                "DemoCubeGameRuntime");

            EnsureRegistered<PulseTargetsGameModule>(
                registry,
                PulseTargetsGameConfig.DefaultGameId,
                "PulseTargetsGameRuntime");
        }

        private static void EnsureRegistered<TModule>(
            GameRegistryService registry,
            string gameId,
            string hostObjectName)
            where TModule : MonoBehaviour, IGameModule
        {
            if (registry == null || string.IsNullOrWhiteSpace(gameId))
            {
                return;
            }

            if (registry.TryResolve(gameId, out _))
            {
                return;
            }

            var module = Object.FindFirstObjectByType<TModule>();
            if (module == null)
            {
                var host = new GameObject(string.IsNullOrWhiteSpace(hostObjectName)
                    ? typeof(TModule).Name
                    : hostObjectName);
                module = host.AddComponent<TModule>();
            }

            registry.RegisterRuntime(gameId, module);
        }
    }
}
