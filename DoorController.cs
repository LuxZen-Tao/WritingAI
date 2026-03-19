using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
	using UnityEditor;
#endif

public class DoorController : MonoBehaviour
{
    [Header("Door State")]
    [SerializeField] private bool startsOpen = false;
    [SerializeField] private bool startsLocked = false;
    [SerializeField] private string requiredKeyId = "";
    [SerializeField] private bool toggleOnInteract = true;

    [Header("Transform Usage")]
    [SerializeField] private bool usePosition = false;
    [SerializeField] private bool useRotation = true;
    [SerializeField] private bool useInitialTransformAsClosed = true;

    [Header("Local Position")]
    [SerializeField] private Vector3 closedLocalPosition;
    [SerializeField] private Vector3 openLocalPosition;

    [Header("Local Rotation (Euler)")]
    [SerializeField] private Vector3 closedLocalEulerAngles;
    [SerializeField] private Vector3 openLocalEulerAngles;

    [Header("Motion")]
    [SerializeField, Min(0f)] private float moveSpeed = 4f;

    [Header("Navigation Blocking")]
    [SerializeField] private Collider blockingCollider;
    [SerializeField] private NavMeshObstacle navObstacle;
    [ContextMenu("Test Toggle Door")]
    private void EditorTestToggle()
    {
        Toggle();
    }
    


    public bool IsOpen => isOpen;
    public bool IsLocked => isLocked;
    public string RequiredKeyId => requiredKeyId;
    public bool IsTransitioning => isTransitioning;

    private bool isOpen;
    private bool isLocked;
    private bool isTransitioning;

    private Vector3 targetLocalPosition;
    private Quaternion targetLocalRotation;

    private const float PositionEpsilon = 0.001f;
    private const float RotationEpsilon = 0.1f;

    private void Awake()
    {
        if (useInitialTransformAsClosed)
        {
            closedLocalPosition = transform.localPosition;
            closedLocalEulerAngles = transform.localEulerAngles;
        }

        isOpen = startsOpen;
        isLocked = startsLocked;
        SetTargets(isOpen);
        ApplyStateImmediate(isOpen);
        RefreshBlockingState();
    }

	private void Update()
	{

		if (!isTransitioning)
			return;

		if (moveSpeed <= 0f)
		{
			ApplyStateImmediate(isOpen);
			isTransitioning = false;
			return;
		}

		float blend = 1f - Mathf.Exp(-moveSpeed * Time.deltaTime);

		if (usePosition)
		{
			transform.localPosition = Vector3.Lerp(transform.localPosition, targetLocalPosition, blend);
		}

		if (useRotation)
		{
			transform.localRotation = Quaternion.Slerp(transform.localRotation, targetLocalRotation, blend);
		}

		if (HasReachedTarget())
		{
			ApplyStateImmediate(isOpen);
			isTransitioning = false;
		}
	}

    public void Interact()
    {
        if (isLocked)
            return;

        if (toggleOnInteract)
        {
            Toggle();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        SetOpenState(true);
    }

    public void Close()
    {
        SetOpenState(false);
    }

    public void Toggle()
    {
        SetOpenState(!isOpen);
    }

    public bool CanUnlockWithKeyId(string keyId)
    {
        if (!isLocked)
            return true;

        return KeyIdsMatch(requiredKeyId, keyId);
    }

    public bool TryUnlock(string keyId)
    {
        if (!isLocked)
            return true;

        if (!CanUnlockWithKeyId(keyId))
            return false;

        isLocked = false;
        return true;
    }

    public static bool KeyIdsMatch(string requiredId, string offeredId)
    {
        if (string.IsNullOrWhiteSpace(requiredId) || string.IsNullOrWhiteSpace(offeredId))
            return false;

        return string.Equals(requiredId.Trim(), offeredId.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }

    private void SetOpenState(bool newOpenState)
    {
        isOpen = newOpenState;
        SetTargets(isOpen);
        isTransitioning = true;
        RefreshBlockingState();

        if (moveSpeed <= 0f)
        {
            ApplyStateImmediate(isOpen);
            isTransitioning = false;
        }
    }

    private void SetTargets(bool openState)
    {
        targetLocalPosition = openState ? openLocalPosition : closedLocalPosition;
        targetLocalRotation = Quaternion.Euler(openState ? openLocalEulerAngles : closedLocalEulerAngles);
    }

    private void ApplyStateImmediate(bool openState)
    {
        if (usePosition)
        {
            transform.localPosition = openState ? openLocalPosition : closedLocalPosition;
        }

        if (useRotation)
        {
            transform.localRotation = Quaternion.Euler(openState ? openLocalEulerAngles : closedLocalEulerAngles);
        }
    }

    private bool HasReachedTarget()
    {
        bool positionReached = !usePosition || Vector3.Distance(transform.localPosition, targetLocalPosition) <= PositionEpsilon;
        bool rotationReached = !useRotation || Quaternion.Angle(transform.localRotation, targetLocalRotation) <= RotationEpsilon;

        return positionReached && rotationReached;
    }

    public void RefreshBlockingState()
    {
        bool shouldBlock = !isOpen;

        if (blockingCollider != null)
        {
            blockingCollider.enabled = shouldBlock;
        }

        if (navObstacle != null)
        {
            navObstacle.enabled = shouldBlock;
        }
    }
}
