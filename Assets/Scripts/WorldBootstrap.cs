using UnityEngine;

namespace AgesOfConflict
{
    public static class WorldBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Object.FindAnyObjectByType<SimulationManager>() == null)
            {
                GameObject simObj = new GameObject("Simulation_Manager");
                simObj.AddComponent<SimulationManager>();
                Debug.Log("[AgesOfConflict] WorldBootstrap automatically initialized SimulationManager.");
            }
        }
    }
}
