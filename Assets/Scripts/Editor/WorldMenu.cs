using UnityEditor;
using UnityEngine;

namespace AgesOfConflict.Editor
{
    public static class WorldMenu
    {
        [MenuItem("Ages of Conflict/Setup Simulation Object in Scene")]
        public static void SetupScene()
        {
            SimulationManager sim = Object.FindAnyObjectByType<SimulationManager>();
            if (sim == null)
            {
                GameObject obj = new GameObject("Simulation_Manager");
                sim = obj.AddComponent<SimulationManager>();
                Undo.RegisterCreatedObjectUndo(obj, "Create Simulation Manager");
                Selection.activeGameObject = obj;
                Debug.Log("Created Simulation_Manager in scene.");
            }
            else
            {
                Selection.activeGameObject = sim.gameObject;
                Debug.Log("Simulation_Manager already exists in scene.");
            }
        }
    }
}
