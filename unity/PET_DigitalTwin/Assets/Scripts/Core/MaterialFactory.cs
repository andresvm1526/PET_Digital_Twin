// MaterialFactory.cs
// Creates and caches Unity Materials for each machine state.
// All colours are defined here as the single source of truth.
// PlantManager calls AssignStateMaterials() after building each machine.

using UnityEngine;

namespace PETDigitalTwin
{
    public static class MaterialFactory
    {
        // ── Colour palette ───────────────────────────────────────────
        public static readonly Color ColourIdle        = new(0.55f, 0.55f, 0.55f); // grey
        public static readonly Color ColourWaiting     = new(1.00f, 0.85f, 0.00f); // amber
        public static readonly Color ColourRunning     = new(0.10f, 0.85f, 0.15f); // green
        public static readonly Color ColourFault       = new(0.95f, 0.05f, 0.05f); // red
        public static readonly Color ColourMaintenance = new(1.00f, 0.45f, 0.00f); // orange
        public static readonly Color ColourFinished    = new(0.10f, 0.40f, 0.95f); // blue

        // ── Cached materials (shared instances) ──────────────────────
        private static Material _matIdle;
        private static Material _matWaiting;
        private static Material _matRunning;
        private static Material _matFault;
        private static Material _matMaintenance;
        private static Material _matFinished;

        public static Material GetIdle()        => _matIdle        ??= Create("State_Idle",        ColourIdle);
        public static Material GetWaiting()     => _matWaiting     ??= Create("State_Waiting",     ColourWaiting);
        public static Material GetRunning()     => _matRunning     ??= Create("State_Running",     ColourRunning);
        public static Material GetFault()       => _matFault       ??= Create("State_Fault",       ColourFault);
        public static Material GetMaintenance() => _matMaintenance ??= Create("State_Maintenance", ColourMaintenance);
        public static Material GetFinished()    => _matFinished    ??= Create("State_Finished",    ColourFinished);

        // ── Assigns state materials to a MachineController ───────────
        public static void AssignStateMaterials(MachineController ctrl)
        {
            // Each machine gets its own material instance to allow individual blinking
            ctrl.matIdle        = new Material(GetIdle())        { name = $"{ctrl.machineId}_Idle" };
            ctrl.matWaiting     = new Material(GetWaiting())     { name = $"{ctrl.machineId}_Waiting" };
            ctrl.matRunning     = new Material(GetRunning())     { name = $"{ctrl.machineId}_Running" };
            ctrl.matFault       = new Material(GetFault())       { name = $"{ctrl.machineId}_Fault" };
            ctrl.matMaintenance = new Material(GetMaintenance()) { name = $"{ctrl.machineId}_Maintenance" };
            ctrl.matFinished    = new Material(GetFinished())    { name = $"{ctrl.machineId}_Finished" };
        }

        // ── Private helpers ──────────────────────────────────────────
        private static Material Create(string name, Color colour)
        {
            // Uses the built-in Standard shader (no paid packages required)
            var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse"))
            {
                name  = name,
                color = colour
            };

            // Enable metallic / smoothness for a slightly industrial look
            mat.SetFloat("_Metallic",   0.3f);
            mat.SetFloat("_Glossiness", 0.5f);

            return mat;
        }
    }
}
