// InjectionMachineAnimation.cs
// Drives the IMM-101 procedural animation sequence:
//   1. Mold Close  → top half descends
//   2. Injection   → material colour pulse (hot orange)
//   3. Cooling     → material colour pulse (blue tint)
//   4. Mold Open   → top half ascends
//   5. Ejection    → preform object flies out
// The API contract exposes operational state/progress, not live utilization.

using System.Collections;
using UnityEngine;
using PETDigitalTwin.Core;

namespace PETDigitalTwin
{
    [DisallowMultipleComponent]
    public class InjectionMachineAnimation : MonoBehaviour, IMachineAnimation
    {
        // ── Procedural sub-parts (created in Start) ───────────────────
        private Transform _moldTop;
        private Transform _moldBottom;
        private Renderer  _moldTopRenderer;

        private Vector3 _moldTopOpenPos;
        private Vector3 _moldTopClosedPos;

        // ── Animation state ──────────────────────────────────────────
        private Coroutine  _cycleCoroutine;
        private bool       _isRunning;
        private float      _speedMultiplier = 1f;

        // ── Preform pool ─────────────────────────────────────────────
        [Header("Preform Prefab (optional – created procedurally if null)")]
        public GameObject preformPrefab;
        public Transform  ejectPoint;

        // ── Colours ──────────────────────────────────────────────────
        private static readonly Color ColourInjection = new(1f, 0.4f, 0f);   // hot orange
        private static readonly Color ColourCooling   = new(0.4f, 0.7f, 1f); // ice blue
        private static readonly Color ColourNeutral   = new(0.8f, 0.8f, 0.8f);

        private Color _originalTopColour;

        // ── Unity lifecycle ──────────────────────────────────────────
        private void Start()
        {
            BuildMoldParts();
        }

        private void OnDestroy()
        {
            StopCycle();
        }

        // ── IMachineAnimation ────────────────────────────────────────
        public void OnStateChanged(MachineState state)
        {
            _speedMultiplier = 1f;

            bool shouldRun = state.OperationalState == MachineOperationalState.RUNNING;

            if (shouldRun && !_isRunning)
                StartCycle();
            else if (!shouldRun && _isRunning)
                StopCycle();
        }

        // ── Cycle ────────────────────────────────────────────────────
        private void StartCycle()
        {
            _isRunning = true;
            _cycleCoroutine = StartCoroutine(CycleRoutine());
        }

        private void StopCycle()
        {
            _isRunning = false;
            if (_cycleCoroutine != null)
            {
                StopCoroutine(_cycleCoroutine);
                _cycleCoroutine = null;
            }
            ResetMold();
        }

        private IEnumerator CycleRoutine()
        {
            while (_isRunning)
            {
                float speed = _speedMultiplier;

                // 1 ── Mold Close
                yield return AnimateMoldClose(duration: 0.6f / speed);

                // 2 ── Injection
                yield return FlashColour(_moldTopRenderer, ColourInjection, duration: 0.5f / speed);

                // 3 ── Cooling
                yield return FlashColour(_moldTopRenderer, ColourCooling,   duration: 0.8f / speed);

                // 4 ── Mold Open
                yield return AnimateMoldOpen(duration: 0.6f / speed);

                // 5 ── Eject preform
                yield return EjectPreform();

                // Brief pause before next cycle
                yield return new WaitForSeconds(0.3f / speed);
            }
        }

        // ── Sub-animations ───────────────────────────────────────────
        private IEnumerator AnimateMoldClose(float duration)
        {
            yield return MoveTo(_moldTop, _moldTopClosedPos, duration);
        }

        private IEnumerator AnimateMoldOpen(float duration)
        {
            yield return MoveTo(_moldTop, _moldTopOpenPos, duration);
        }

        private IEnumerator MoveTo(Transform t, Vector3 target, float duration)
        {
            Vector3 start = t.localPosition;
            float   elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                t.localPosition = Vector3.Lerp(start, target, elapsed / duration);
                yield return null;
            }
            t.localPosition = target;
        }

        private IEnumerator FlashColour(Renderer r, Color target, float duration)
        {
            if (r == null) { yield return new WaitForSeconds(duration); yield break; }

            Color start = r.material.color;
            float elapsed = 0f;

            // Ramp to target colour
            while (elapsed < duration * 0.3f)
            {
                elapsed += Time.deltaTime;
                r.material.color = Color.Lerp(start, target, elapsed / (duration * 0.3f));
                yield return null;
            }

            // Hold
            yield return new WaitForSeconds(duration * 0.4f);

            // Ramp back
            elapsed = 0f;
            float rampDur = duration * 0.3f;
            while (elapsed < rampDur)
            {
                elapsed += Time.deltaTime;
                r.material.color = Color.Lerp(target, _originalTopColour, elapsed / rampDur);
                yield return null;
            }
            r.material.color = _originalTopColour;
        }

        private IEnumerator EjectPreform()
        {
            GameObject pf = preformPrefab != null
                ? Instantiate(preformPrefab)
                : CreatePreformPrimitive();

            Vector3 origin = ejectPoint != null
                ? ejectPoint.position
                : _moldBottom.position + Vector3.up * 0.3f;

            pf.transform.position = origin;

            // Slide out to the right
            float t = 0f;
            float dur = 0.5f;
            Vector3 dest = origin + new Vector3(1.5f, -0.5f, 0f);

            while (t < dur)
            {
                t += Time.deltaTime;
                pf.transform.position = Vector3.Lerp(origin, dest, t / dur);
                yield return null;
            }

            Destroy(pf, 0.5f);
        }

        // ── Scene setup ──────────────────────────────────────────────
        private void BuildMoldParts()
        {
            // Bottom half
            _moldBottom = CreateHalf("MoldBottom", new Vector3(0f, -0.2f, 0f), new Vector3(2.4f, 1f, 1.6f), new Color(0.6f, 0.6f, 0.65f));

            // Top half (will move)
            _moldTop = CreateHalf("MoldTop", new Vector3(0f, 1.2f, 0f), new Vector3(2.4f, 1f, 1.6f), new Color(0.55f, 0.55f, 0.6f));
            _moldTopRenderer  = _moldTop.GetComponent<Renderer>();
            _originalTopColour = _moldTopRenderer.material.color;

            // Key positions
            _moldTopOpenPos   = _moldTop.localPosition;
            _moldTopClosedPos = new Vector3(0f, 0.2f, 0f);

            // Eject point default
            if (ejectPoint == null)
            {
                var ep = new GameObject("EjectPoint");
                ep.transform.SetParent(transform, false);
                ep.transform.localPosition = new Vector3(1.5f, 0f, 0f);
                ejectPoint = ep.transform;
            }
        }

        private Transform CreateHalf(string partName, Vector3 localPos, Vector3 scale, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = scale;
            go.GetComponent<Renderer>().material.color = colour;
            return go.transform;
        }

        private static GameObject CreatePreformPrimitive()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Preform";
            go.transform.localScale = new Vector3(0.1f, 0.2f, 0.1f);
            go.GetComponent<Renderer>().material.color = new Color(0.85f, 0.95f, 1f, 0.8f);
            return go;
        }

        private void ResetMold()
        {
            if (_moldTop != null)
                _moldTop.localPosition = _moldTopOpenPos;
            if (_moldTopRenderer != null)
                _moldTopRenderer.material.color = _originalTopColour;
        }
    }
}
