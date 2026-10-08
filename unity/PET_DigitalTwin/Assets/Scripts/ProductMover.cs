// ProductMover.cs
// Manages pooled product objects (pellets, preforms, bottles) that travel
// along conveyor paths between machines.
// Conveyor speed is linked to the backend state of CV-101 / SBM-101.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PETDigitalTwin.Core;

namespace PETDigitalTwin
{
    /// <summary>
    /// Attach to a Conveyor GameObject (e.g. CV-101) to animate products along it.
    /// </summary>
    public class ProductMover : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────
        [Header("Product Prefab")]
        public GameObject productPrefab;

        [Header("Path Waypoints (world-space)")]
        [Tooltip("Fill with empty GameObjects marking the path. If empty, uses machine start→end.")]
        public Transform[] waypoints;

        [Header("Conveyor settings")]
        public float baseSpeed      = 1.5f;   // m/s at utilization = 1.0
        public int   poolSize       = 8;
        public float spawnInterval  = 0.5f;   // seconds between spawns at baseSpeed

        // ── State ────────────────────────────────────────────────────
        private readonly Queue<GameObject> _pool    = new();
        private readonly List<ProductAgent> _active = new();
        private float  _currentSpeed;
        private bool   _running;
        private Coroutine _spawnRoutine;

        // ── Unity lifecycle ──────────────────────────────────────────
        private void Start()
        {
            PrewarmPool();
            _currentSpeed = baseSpeed;
        }

        // ── Public API ───────────────────────────────────────────────
        public void ApplyState(MachineState state)
        {
            _currentSpeed = baseSpeed;
            bool shouldRun = state.OperationalState == MachineOperationalState.RUNNING;

            if (shouldRun && !_running)
            {
                _running = true;
                _spawnRoutine = StartCoroutine(SpawnLoop());
            }
            else if (!shouldRun && _running)
            {
                _running = false;
                if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
                ReturnAllToPool();
            }

            // Update speed for all active products
            foreach (var agent in _active)
                agent.Speed = _currentSpeed;
        }

        // ── Pool ─────────────────────────────────────────────────────
        private void PrewarmPool()
        {
            for (int i = 0; i < poolSize; i++)
            {
                var go = productPrefab != null
                    ? Instantiate(productPrefab, transform)
                    : CreateDefaultProduct();
                go.SetActive(false);
                _pool.Enqueue(go);
            }
        }

        private GameObject GetFromPool()
        {
            if (_pool.Count > 0)
            {
                var go = _pool.Dequeue();
                go.SetActive(true);
                return go;
            }
            // Pool exhausted – create extra
            var extra = productPrefab != null
                ? Instantiate(productPrefab, transform)
                : CreateDefaultProduct();
            return extra;
        }

        private void ReturnToPool(GameObject go)
        {
            go.SetActive(false);
            _pool.Enqueue(go);
        }

        private void ReturnAllToPool()
        {
            foreach (var agent in _active)
                ReturnToPool(agent.GO);
            _active.Clear();
        }

        // ── Spawn loop ───────────────────────────────────────────────
        private IEnumerator SpawnLoop()
        {
            while (_running)
            {
                SpawnProduct();
                float interval = _currentSpeed > 0f ? spawnInterval / _currentSpeed * baseSpeed : spawnInterval;
                yield return new WaitForSeconds(Mathf.Clamp(interval, 0.1f, 5f));
            }
        }

        private void SpawnProduct()
        {
            if (waypoints == null || waypoints.Length < 2) return;

            var go    = GetFromPool();
            var agent = new ProductAgent(go, waypoints, _currentSpeed, () =>
            {
                ReturnToPool(go);
                _active.Remove(_active.Find(a => a.GO == go));
            });
            _active.Add(agent);
            StartCoroutine(MoveProductCoroutine(agent));
        }

        private IEnumerator MoveProductCoroutine(ProductAgent agent)
        {
            while (!agent.Finished)
            {
                agent.Update(Time.deltaTime);
                agent.GO.transform.position = agent.Position;
                yield return null;
            }
        }

        // ── Default product primitive ────────────────────────────────
        private static GameObject CreateDefaultProduct()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Product";
            go.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
            go.GetComponent<Renderer>().material.color = new Color(0.7f, 0.92f, 1f, 0.85f);
            // Remove collider to avoid physics interaction
            Destroy(go.GetComponent<Collider>());
            return go;
        }

        // ── Inner agent ──────────────────────────────────────────────
        private class ProductAgent
        {
            public GameObject GO;
            public float      Speed;
            public bool       Finished { get; private set; }
            public Vector3    Position { get; private set; }

            private readonly Transform[] _waypoints;
            private int   _nextWP;
            private readonly System.Action _onComplete;

            public ProductAgent(GameObject go, Transform[] waypoints, float speed, System.Action onComplete)
            {
                GO          = go;
                Speed       = speed;
                _waypoints  = waypoints;
                _onComplete = onComplete;
                _nextWP     = 1;
                Position    = waypoints[0].position;
                go.transform.position = Position;
            }

            public void Update(float dt)
            {
                if (Finished) return;

                Vector3 target = _waypoints[_nextWP].position;
                float   dist   = Speed * dt;
                Position = Vector3.MoveTowards(Position, target, dist);

                if (Vector3.Distance(Position, target) < 0.01f)
                {
                    _nextWP++;
                    if (_nextWP >= _waypoints.Length)
                    {
                        Finished = true;
                        _onComplete?.Invoke();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Scrolls the UV of a conveyor belt material to simulate belt movement.
    /// Attach to the belt Renderer GameObject.
    /// </summary>
    public class ConveyorVisual : MonoBehaviour
    {
        [Tooltip("UV scroll speed.")]
        public float scrollSpeed = 0.3f;

        private Renderer _r;
        private bool     _active = true;

        private void Start()   => _r = GetComponentInChildren<Renderer>();

        private void Update()
        {
            if (!_active || _r == null) return;
            float offset = Time.time * scrollSpeed % 1f;
            _r.material.mainTextureOffset = new Vector2(offset, 0f);
        }

        public void SetRunning(bool running) => _active = running;
    }
}
