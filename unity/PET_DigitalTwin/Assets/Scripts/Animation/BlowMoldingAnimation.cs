// BlowMoldingAnimation.cs
// Drives the SBM-101 procedural animation sequence:
//   1. Preform enters (slides in from the left)
//   2. Mold close  (two halves converge)
//   3. Blow / Stretch (scale expansion to simulate bottle growth)
//   4. Mold open  (halves diverge)
//   5. Bottle exits (slides out to the right)
// The API contract exposes operational state/progress, not live utilization.

using System.Collections;
using UnityEngine;
using PETDigitalTwin.Core;

namespace PETDigitalTwin
{
    [DisallowMultipleComponent]
    public class BlowMoldingAnimation : MonoBehaviour, IMachineAnimation
    {
        // ── Mold halves ──────────────────────────────────────────────
        private Transform _leftHalf;
        private Transform _rightHalf;

        private Vector3 _leftOpen;
        private Vector3 _rightOpen;
        private Vector3 _leftClosed;
        private Vector3 _rightClosed;

        // ── Speed ────────────────────────────────────────────────────
        private float     _speedMultiplier = 1f;
        private Coroutine _cycleCoroutine;
        private bool      _isRunning;

        // ── Prefabs ──────────────────────────────────────────────────
        [Header("Prefabs (optional – created procedurally if null)")]
        public GameObject preformPrefab;
        public GameObject bottlePrefab;

        // ── Colours ──────────────────────────────────────────────────
        private static readonly Color ColourBlowing = new(0.3f, 0.8f, 1f); // air blue

        // ── Unity lifecycle ──────────────────────────────────────────
        private void Start()
        {
            BuildMoldHalves();
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

            if (shouldRun && !_isRunning)  StartCycle();
            else if (!shouldRun && _isRunning) StopCycle();
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
            OpenMoldImmediate();
        }

        private IEnumerator CycleRoutine()
        {
            while (_isRunning)
            {
                float spd = _speedMultiplier;

                // 1 ── Preform enters
                yield return SlideIn(preformPrefab != null
                    ? Instantiate(preformPrefab)
                    : CreatePreformPrimitive(), spd);

                // 2 ── Mold close
                yield return CloseMold(0.5f / spd);

                // Hold closed briefly before blow
                yield return new WaitForSeconds(0.1f);

                // 3 ── Blow / Stretch
                GameObject bottle = preformPrefab != null
                    ? Instantiate(bottlePrefab ?? preformPrefab)
                    : CreateBottlePrimitive();
                bottle.transform.position = transform.position;
                yield return ExpandBottle(bottle, 0.7f / spd);

                // 4 ── Mold open
                yield return OpenMold(0.5f / spd);

                // 5 ── Bottle exits
                yield return SlideOut(bottle, spd);

                yield return new WaitForSeconds(0.2f / spd);
            }
        }

        // ── Sub-animations ───────────────────────────────────────────
        private IEnumerator SlideIn(GameObject obj, float spd)
        {
            if (obj == null) yield break;

            Vector3 start = transform.position + new Vector3(-2f, 0f, 0f);
            Vector3 end   = transform.position;
            obj.transform.position = start;

            float dur = 0.4f / spd;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                obj.transform.position = Vector3.Lerp(start, end, t / dur);
                yield return null;
            }
            Destroy(obj); // replaced by bottle after blow
        }

        private IEnumerator CloseMold(float duration)
        {
            yield return MoveHalves(_leftClosed, _rightClosed, duration);
        }

        private IEnumerator OpenMold(float duration)
        {
            yield return MoveHalves(_leftOpen, _rightOpen, duration);
        }

        private void OpenMoldImmediate()
        {
            if (_leftHalf  != null) _leftHalf.localPosition  = _leftOpen;
            if (_rightHalf != null) _rightHalf.localPosition = _rightOpen;
        }

        private IEnumerator MoveHalves(Vector3 targetL, Vector3 targetR, float duration)
        {
            Vector3 startL = _leftHalf.localPosition;
            Vector3 startR = _rightHalf.localPosition;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                _leftHalf.localPosition  = Vector3.Lerp(startL, targetL, t);
                _rightHalf.localPosition = Vector3.Lerp(startR, targetR, t);
                yield return null;
            }

            _leftHalf.localPosition  = targetL;
            _rightHalf.localPosition = targetR;
        }

        private IEnumerator ExpandBottle(GameObject bottle, float duration)
        {
            if (bottle == null) yield break;

            // Tint the mold blue during blow
            Color origL = _leftHalf .GetComponent<Renderer>().material.color;
            Color origR = _rightHalf.GetComponent<Renderer>().material.color;
            _leftHalf .GetComponent<Renderer>().material.color = ColourBlowing;
            _rightHalf.GetComponent<Renderer>().material.color = ColourBlowing;

            Vector3 startScale = new Vector3(0.05f, 0.05f, 0.05f);
            Vector3 endScale   = new Vector3(0.25f, 0.55f, 0.25f);
            bottle.transform.localScale = startScale;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                bottle.transform.localScale = Vector3.Lerp(startScale, endScale, elapsed / duration);
                yield return null;
            }
            bottle.transform.localScale = endScale;

            _leftHalf .GetComponent<Renderer>().material.color = origL;
            _rightHalf.GetComponent<Renderer>().material.color = origR;
        }

        private IEnumerator SlideOut(GameObject bottle, float spd)
        {
            if (bottle == null) yield break;

            Vector3 start = bottle.transform.position;
            Vector3 end   = start + new Vector3(2.5f, -0.3f, 0f);
            float dur = 0.5f / spd;
            float t = 0f;

            while (t < dur)
            {
                t += Time.deltaTime;
                bottle.transform.position = Vector3.Lerp(start, end, t / dur);
                yield return null;
            }

            Destroy(bottle, 0.3f);
        }

        // ── Scene setup ──────────────────────────────────────────────
        private void BuildMoldHalves()
        {
            _leftHalf  = CreateHalf("MoldLeft",  new Vector3(-0.8f, 0f, 0f), new Color(0.5f, 0.55f, 0.6f));
            _rightHalf = CreateHalf("MoldRight", new Vector3( 0.8f, 0f, 0f), new Color(0.5f, 0.55f, 0.6f));

            _leftOpen   = _leftHalf.localPosition;
            _rightOpen  = _rightHalf.localPosition;
            _leftClosed  = new Vector3(-0.1f, 0f, 0f);
            _rightClosed = new Vector3( 0.1f, 0f, 0f);
        }

        private Transform CreateHalf(string partName, Vector3 localPos, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = new Vector3(1.2f, 2f, 1.6f);
            go.GetComponent<Renderer>().material.color = colour;
            return go.transform;
        }

        private static GameObject CreatePreformPrimitive()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Preform_SBM";
            go.transform.localScale = new Vector3(0.08f, 0.18f, 0.08f);
            go.GetComponent<Renderer>().material.color = new Color(0.9f, 0.95f, 1f, 0.9f);
            return go;
        }

        private static GameObject CreateBottlePrimitive()
        {
            // Approximate a bottle with a cylinder + small sphere on top
            var root = new GameObject("PETBottle");

            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.25f, 0.5f, 0.25f);
            body.GetComponent<Renderer>().material.color = new Color(0.7f, 0.92f, 1f, 0.75f);

            var neck = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            neck.transform.SetParent(root.transform, false);
            neck.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            neck.transform.localScale    = new Vector3(0.1f, 0.1f, 0.1f);
            neck.GetComponent<Renderer>().material.color = new Color(0.6f, 0.85f, 1f, 0.75f);

            return root;
        }
    }
}
