using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProductMover : MonoBehaviour
{
    [SerializeField] private Transform[] waypoints;
    [SerializeField, Min(0.1f)] private float speed = 2f;
    [SerializeField, Range(0f, 1f)] private float startOffset;
    [SerializeField] private bool isMoving = true;

    private int nextWaypointIndex;

    public void Configure(Transform[] path, float movementSpeed, float normalizedStartOffset)
    {
        waypoints = path;
        speed = Mathf.Max(0.1f, movementSpeed);
        startOffset = Mathf.Clamp01(normalizedStartOffset);
    }

    public void SetMoving(bool shouldMove)
    {
        isMoving = shouldMove;
    }

    private void Start()
    {
        if (waypoints == null || waypoints.Length < 2)
        {
            enabled = false;
            return;
        }

        int startIndex = Mathf.Min(
            Mathf.FloorToInt(startOffset * waypoints.Length),
            waypoints.Length - 1);

        transform.position = waypoints[startIndex].position;
        nextWaypointIndex = (startIndex + 1) % waypoints.Length;
    }

    private void Update()
    {
        if (!isMoving)
        {
            return;
        }

        Transform target = waypoints[nextWaypointIndex];
        Vector3 direction = target.position - transform.position;

        transform.position = Vector3.MoveTowards(
            transform.position,
            target.position,
            speed * Time.deltaTime);

        if (direction.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        if ((transform.position - target.position).sqrMagnitude > 0.0001f)
        {
            return;
        }

        nextWaypointIndex++;
        if (nextWaypointIndex >= waypoints.Length)
        {
            transform.position = waypoints[0].position;
            nextWaypointIndex = 1;
        }
    }

    private void OnValidate()
    {
        speed = Mathf.Max(0.1f, speed);
        startOffset = Mathf.Clamp01(startOffset);
    }
}
