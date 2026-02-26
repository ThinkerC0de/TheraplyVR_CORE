using TheraplyCore.Games.Runtime;
using UnityEngine;

namespace TheraplyGames.BilateralMarkers
{
    public static class BilateralMarkersRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterRuntime()
        {
            var registry = Object.FindFirstObjectByType<GameRegistryService>();
            if (registry == null)
            {
                return;
            }

            if (registry.TryResolve(BilateralMarkersSceneGameController.DefaultGameId, out _))
            {
                return;
            }

            var controller = Object.FindFirstObjectByType<BilateralMarkersSceneGameController>();
            if (controller == null)
            {
                var host = new GameObject("BilateralMarkersRuntime");
                controller = host.AddComponent<BilateralMarkersSceneGameController>();
            }

            registry.RegisterRuntime(BilateralMarkersSceneGameController.DefaultGameId, controller);
        }
    }
}
