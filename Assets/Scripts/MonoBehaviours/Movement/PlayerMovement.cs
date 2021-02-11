using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
	#region Editor Parameters

	[Header("Avatar Settings")]
	[SerializeField]					private Transform cameraInputSpace = default;
	[SerializeField]					private Transform avatar = default;

	[Header("Speed Settings")]
	[SerializeField, Range(0f, 100f)]	private float maxSpeed = 10f;
	[SerializeField, Range(0f, 100f)]	private float maxClimbSpeed = 2f;
	[SerializeField, Range(0f, 100f)]	private float maxAcceleration = 10f;
	[SerializeField, Range(0f, 100f)]	private float maxAirAcceleration = 1f;
	[SerializeField, Range(0f, 100f)]	private float maxClimbAcceleration = 20f;

	[Header("Jump Settings")]
	[SerializeField, Range(0f, 10f)]	private float jumpHeight = 2f;
	[SerializeField, Range(0, 5)]		private int maxAirJumps = 0;

	[Header("Slopes Settings")]
	[SerializeField, Range(0, 90)]		private float maxGroundAngle = 25f;
	[SerializeField, Range(0, 90)]		private float maxStairsAngle = 50f;
	[SerializeField, Range(90, 180)]	private float maxClimbAngle = 100f;
	[SerializeField, Range(0f, 100f)]	private float maxSnapSpeed = 100f;
	[SerializeField, Min(0f)]			private float probeDistance = 1f;

	[Header("Layers Settings")]
	[SerializeField]					private LayerMask probeMask = -1;
	[SerializeField]					private LayerMask stairsMask = -1;
	[SerializeField]					private LayerMask climbMask = -1;

    #endregion

    Animator avatarAnimator;

	Rigidbody body, connectedBody, previousConnectedBody;

	Vector2 playerInput;
	Vector3 velocity, connectionVelocity;
	Vector3 upAxis, rightAxis, forwardAxis;
	Vector3 contactNormal, steepNormal, climbNormal;
	Vector3 lastClimbNormal, lastContactNormal, lastSteepNormal;
	Vector3 lastConnectionVelocity, connectionWorldPosition, connectionLocalPosition;

	bool desiredJump, desiredClimbing;

	int groundContactCount, steepContactCount, climbContactCount;
	int stepsSinceLastGrounded, stepsSinceLastJump;
	int jumpPhase;

	float minGroundDotProduct, minStairsDotProduct, minClimbDotProduct;

    bool OnGround => groundContactCount > 0;
	
	bool OnSteep => steepContactCount > 0;

	bool Climbing => climbContactCount > 0 && stepsSinceLastJump > 2;

	public void PreventSnapToGround()
    {
		stepsSinceLastJump = -1;
    }

	void Awake()
	{
		avatarAnimator = GetComponentInChildren<Animator>();
		body = GetComponent<Rigidbody>();
		body.useGravity = false;
		OnValidate();
	}

	void OnValidate()
	{
		minGroundDotProduct = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
		minStairsDotProduct = Mathf.Cos(maxStairsAngle * Mathf.Deg2Rad);
		minClimbDotProduct = Mathf.Cos(maxClimbAngle * Mathf.Deg2Rad);
	}

	void Update()
	{
		playerInput.x = Input.GetAxis("Horizontal");
		playerInput.y = Input.GetAxis("Vertical");

		playerInput = Vector3.ClampMagnitude(playerInput, 1f);

		if (cameraInputSpace)
		{
			rightAxis = ProjectDirectionOnPlane(cameraInputSpace.right, upAxis);
			forwardAxis = ProjectDirectionOnPlane(cameraInputSpace.forward, upAxis);
		}
		else
		{
			rightAxis = ProjectDirectionOnPlane(Vector3.right, upAxis);
			forwardAxis = ProjectDirectionOnPlane(Vector3.forward, upAxis);
		}

		desiredJump |= Input.GetButtonDown("Jump");
		desiredClimbing = Input.GetButton("Climb");

		UpdateAvatar();
	}

	void UpdateAvatar()
    {
        Vector3 rotationPlaneNormal = lastContactNormal;

        if (Climbing)
        {
			//TODO: Do something
        }
		else if (!OnGround)
        {
            if (OnSteep)
            {
				lastContactNormal = lastSteepNormal;
            }
        }

		Vector3 movement = (body.velocity - lastConnectionVelocity) * Time.deltaTime;
		movement -= rotationPlaneNormal * Vector3.Dot(movement, rotationPlaneNormal);
		float distance = movement.magnitude;

		Quaternion rotation = avatar.localRotation;
		if (connectedBody && connectedBody == previousConnectedBody)
		{
			rotation = Quaternion.Euler(connectedBody.angularVelocity * Time.deltaTime) * rotation;
			if (distance < 0.001f)
			{
				avatar.localRotation = rotation;
				return;
			}
		}
		else if (distance < 0.001f)
		{
			return;
		}

		avatar.transform.eulerAngles = upAxis * Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;

        avatarAnimator.SetFloat("speedPercent", playerInput.magnitude);
    }

	void FixedUpdate()
	{
		Vector3 gravity = CustomGravity.GetGravity(body.position, out upAxis);

		UpdateState();
		AdjustVelocity();

		if (desiredJump)
		{
			desiredJump = false;
			Jump(gravity);
		}

        if (Climbing)
        {
			velocity -= contactNormal * (maxClimbAcceleration * 0.9f * Time.deltaTime);
        }
		else if(OnGround && velocity.sqrMagnitude < 0.01f)
        {
			velocity += contactNormal * (Vector3.Dot(gravity, contactNormal) * Time.deltaTime);
        }
		else if(desiredClimbing && OnGround)
        {
			velocity += (gravity - contactNormal * (maxClimbAcceleration * 0.9f)) * Time.deltaTime;
        }
        else
        {
			velocity += gravity * Time.deltaTime;
        }

		body.velocity = velocity;
		ClearState();
	}

    void ClearState()
	{
		lastContactNormal = contactNormal;
		lastSteepNormal = steepNormal;
		lastConnectionVelocity = connectionVelocity;
		groundContactCount = steepContactCount = climbContactCount = 0;
		contactNormal = steepNormal = climbNormal = Vector3.zero;
		connectionVelocity = Vector3.zero;
		previousConnectedBody = connectedBody;
		connectedBody = null;
	}

	void UpdateState()
	{
		stepsSinceLastGrounded += 1;
		stepsSinceLastJump += 1;
		velocity = body.velocity;
		if (CheckClimbing() || OnGround || SnapToGround() || CheckSteepContacts())
		{
			stepsSinceLastGrounded = 0;
			if (stepsSinceLastJump > 1)
			{
				jumpPhase = 0;
			}
			if (groundContactCount > 1)
			{
				contactNormal.Normalize();
			}
		}
		else
		{
			contactNormal = upAxis;
		}

        if (connectedBody)
        {
			if(connectedBody.isKinematic || connectedBody.mass >= body.mass)
            {
				UpdateConnectionState();
            }
        }
	}

	void UpdateConnectionState()
    {
		if(connectedBody == previousConnectedBody)
        {
			Vector3 connectionMovement = connectedBody.transform.TransformPoint(connectionLocalPosition) - connectionWorldPosition;
			connectionVelocity = connectionMovement / Time.deltaTime;
        }
		connectionWorldPosition = body.position;
		connectionLocalPosition = connectedBody.transform.InverseTransformPoint(connectionWorldPosition);
    }

	bool CheckClimbing()
    {
        if (Climbing)
        {
			if(climbContactCount > 1)
            {
				climbNormal.Normalize();
				float upDot = Vector3.Dot(upAxis, climbNormal);
				if(upDot >= minGroundDotProduct)
                {
					climbNormal = lastClimbNormal;
                }
            }
			groundContactCount = 1;
			contactNormal = climbNormal;
			return true;
        }
		return false;
    }

	bool SnapToGround()
	{
		if (stepsSinceLastGrounded > 1 || stepsSinceLastJump <= 2)
		{
			return false;
		}
		float speed = velocity.magnitude;
		if (speed > maxSnapSpeed)
		{
			return false;
		}
		if (!Physics.Raycast(
			body.position, -upAxis, out RaycastHit hit,
			probeDistance, probeMask, QueryTriggerInteraction.Ignore
		))
		{
			return false;
		}

		float upDot = Vector3.Dot(upAxis, hit.normal);
		if (upDot < GetMinDot(hit.collider.gameObject.layer))
		{
			return false;
		}

		groundContactCount = 1;
		contactNormal = hit.normal;
		float dot = Vector3.Dot(velocity, hit.normal);
		if (dot > 0f)
		{
			velocity = (velocity - hit.normal * dot).normalized * speed;
		}

		connectedBody = hit.rigidbody;
		return true;
	}

	bool CheckSteepContacts()
	{
		if (steepContactCount > 1)
		{
			steepNormal.Normalize();
			float upDot = Vector3.Dot(upAxis, steepNormal);
			if (upDot >= minGroundDotProduct)
			{
				steepContactCount = 0;
				groundContactCount = 1;
				contactNormal = steepNormal;
				return true;
			}
		}
		return false;
	}

	void AdjustVelocity()
	{
		float acceleration, speed;
		Vector3 xAxis, zAxis;
        if (Climbing)
        {
			acceleration = maxClimbAcceleration;
			speed = maxClimbSpeed;
			xAxis = Vector3.Cross(contactNormal, upAxis);
			zAxis = upAxis;
        }
        else
        {
			acceleration = OnGround ? maxAcceleration : maxAirAcceleration;
			speed = OnGround && desiredClimbing ? maxClimbSpeed : maxSpeed;
			xAxis = rightAxis;
			zAxis = forwardAxis;
        }
		xAxis = ProjectDirectionOnPlane(xAxis, contactNormal);
		zAxis = ProjectDirectionOnPlane(zAxis, contactNormal);

		Vector3 relativeVelocity = velocity - connectionVelocity;

		Vector3 adjustment;
		adjustment.x = playerInput.x * speed - Vector3.Dot(relativeVelocity, xAxis);
		adjustment.z = playerInput.y * speed - Vector3.Dot(relativeVelocity, zAxis);

		adjustment.y = 0f;

		adjustment = Vector3.ClampMagnitude(adjustment, acceleration * Time.deltaTime);

		velocity += xAxis * adjustment.x + zAxis * adjustment.z;

	}

	void Jump(Vector3 gravity)
	{
		Vector3 jumpDirection;
		if (OnGround)
		{
			jumpDirection = contactNormal;
		}
		else if (OnSteep)
		{
			jumpDirection = steepNormal;
			jumpPhase = 0;
		}
		else if (maxAirJumps > 0 && jumpPhase <= maxAirJumps)
		{
			if (jumpPhase == 0)
			{
				jumpPhase = 1;
			}
			jumpDirection = contactNormal;
		}
		else
		{
			return;
		}

		stepsSinceLastJump = 0;
		jumpPhase += 1;
		float jumpSpeed = Mathf.Sqrt(2f * gravity.magnitude * jumpHeight);
		jumpDirection = (jumpDirection + upAxis).normalized;
		float alignedSpeed = Vector3.Dot(velocity, jumpDirection);
		if (alignedSpeed > 0f)
		{
			jumpSpeed = Mathf.Max(jumpSpeed - alignedSpeed, 0f);
		}
		velocity += jumpDirection * jumpSpeed;
	}

	void OnCollisionEnter(Collision collision)
	{
		EvaluateCollision(collision);
	}

	void OnCollisionStay(Collision collision)
	{
		EvaluateCollision(collision);
	}

	void EvaluateCollision(Collision collision)
	{
		int layer = collision.gameObject.layer;
		float minDot = GetMinDot(layer);
		for (int i = 0; i < collision.contactCount; i++)
		{
			Vector3 normal = collision.GetContact(i).normal;
			float upDot = Vector3.Dot(upAxis, normal);
			if (upDot >= minDot)
			{
				groundContactCount += 1;
				contactNormal += normal;
				connectedBody = collision.rigidbody;
			}
			else
            {
				if (upDot > -0.01f)
				{
					steepContactCount += 1;
					steepNormal += normal;
					if (groundContactCount == 0)
					{
						connectedBody = collision.rigidbody;
					}
				}
				if(desiredClimbing && upDot >= minClimbDotProduct && (climbMask & (1 << layer)) != 0)
                {
					climbContactCount += 1;
					climbNormal += normal;
					lastClimbNormal = normal;
					connectedBody = collision.rigidbody;
                }
			}
		}
	}

	Vector3 ProjectDirectionOnPlane(Vector3 direction, Vector3 normal)
	{
		return (direction - normal * Vector3.Dot(direction, normal)).normalized;
	}

	float GetMinDot(int layer)
	{
		return (stairsMask & (1 << layer)) == 0 ?
			minGroundDotProduct : minStairsDotProduct;
	}
}