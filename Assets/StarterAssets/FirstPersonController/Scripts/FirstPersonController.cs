using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using System.Collections;

namespace StarterAssets {

	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class FirstPersonController : MonoBehaviour {
		[Header("Player")]
		[Tooltip("Move speed of the character in m/s")]
		public float MoveSpeed = 4.0f;
		[Tooltip("Sprint speed of the character in m/s")]
		public float SprintSpeed = 6.0f;
		[Tooltip("Rotation speed of the character")]
		public float RotationSpeed = 1.0f;
		[Tooltip("Acceleration and deceleration")]
		public float SpeedChangeRate = 10.0f;

		[Space(10)]
		[Tooltip("The height the player can jump")]
		public float JumpHeight = 1.2f;
		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		public float Gravity = -15.0f;

		[Space(10)]
		[Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
		public float JumpTimeout = 0.1f;
		[Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
		public float FallTimeout = 0.15f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.5f;
		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;

		[Header("Cinemachine")]
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 90.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -90.0f;

		[Header("Smoking")]
		public GameObject cigarette;
		public ParticleSystem smokeParticles;

		public AudioSource audioSource;
		public AudioClip smokeSound, smokeSoundLong;
		public AudioClip[] stepSounds;
		public AudioClip[] hitSounds;
		public AudioClip gunshotSound;
		public AudioClip dryFiringSound;
		public AudioClip reloadSound;
		public Transform gunTransform;
		public TextMeshProUGUI text;

		[Header("Drunken Settings")]
		[Range(0f, 5f)]
		[Tooltip("Controls the intensity of all drunken effects (0 = off, 1 = max)")]
		public float Drunkenness = 1.0f;

		[Header("Drunken Camera Lean")]
		[Tooltip("How much the camera rolls/tilts when moving sideways")]
		public float LeanIntensity = 1f;
		[Tooltip("How fast the camera tilts and recovers")]
		public float LeanSmoothSpeed = 2.0f;

		// cinemachine
		private float _cinemachineTargetPitch;
		private float _cameraRoll;

		// player
		private float _speed;
		private float _rotationVelocity;
		private float _verticalVelocity;
		private float _terminalVelocity = 53.0f;

		// timeout deltatime
		private float _jumpTimeoutDelta;
		private float _fallTimeoutDelta;

		private Vector2 perlinOffset1, perlinOffset2;
		private float time = 0;

#if ENABLE_INPUT_SYSTEM
		private PlayerInput _playerInput;
#endif
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;

		private const float _threshold = 0.01f;

		public static FirstPersonController instance;

		private bool smoking = false, reloading = false;

		// Flinch effect state
		private float _flinchPitchOffset = 0f;
		private float _flinchRollOffset = 0f;
		private Coroutine _flinchCoroutine;

		private float hitCooldown = 2.5f;
		private float lastHitTime = 0f;

		public GameObject hurtText;
		public Image strawberryJam;
		private float health = 100;
		private int enemies = 5;
		private int cigs = 3;

		void updateText() {
			text.text = "Enemies Remaioning: " + enemies + "\nHealth: " + health + "\nCigs Remaining: " + cigs;
		}

		void OnControllerColliderHit(ControllerColliderHit hit) {
			// 1. If we recently hit a wall, don't play the sound again yet (cooldown check)
			if (Time.time < lastHitTime + hitCooldown) return;

			// 2. Filter out the floor.
			// The 'normal.y' represents how flat/vertical the surface is.
			// A floor has a high Y normal (0.7 to 1.0). A wall has a flat Y normal (close to 0).
			if (Mathf.Abs(hit.normal.y) < 0.2f)
			{
				// 3. Play the sound
				if (audioSource != null && hitSounds.Length > 0)
				{
					audioSource.PlayOneShot(hitSounds[Random.Range(0, hitSounds.Length)]);
					lastHitTime = Time.time; // Reset the cooldown timer
				}
			}
		}

		public void TryToSmoke()
		{
			if (!smoking && !reloading)
			{
				StartCoroutine(SmokeRoutine());
			}
		}

		private bool IsCurrentDeviceMouse
		{
			get
			{
				#if ENABLE_INPUT_SYSTEM
				return _playerInput.currentControlScheme == "KeyboardMouse";
				#else
				return false;
				#endif
			}
		}

		public void takeDamage() {
			Debug.Log("Player took damage");
			if (_flinchCoroutine != null) {
				StopCoroutine(_flinchCoroutine);
			}
			_flinchCoroutine = StartCoroutine(FlinchEffect());
			health -= 20;
			updateHealthUi();
		}

		private void updateHealthUi() {
			updateText();
			strawberryJam.color = new Color(1f, 0f, 0f, 0.5f - 0.5f*health / 100f);
			if (health <= 50) {
				hurtText.SetActive(true);
			} else {
				hurtText.SetActive(false);
			}
		}

		private IEnumerator FlinchEffect() {
			float duration = 0.25f; // Duration of the flinch
			float elapsed = 0f;

			// Kick camera up slightly (negative pitch) and tilt roll left/right
			float targetPitchKick = Random.Range(-4f, 6f);
			float targetRollKick = Random.Range(-5f, 5f);

			while (elapsed < duration) {
				elapsed += Time.deltaTime;
				float t = elapsed / duration;

				// Fast spike, then smooth decay back to zero
				float intensity;
				if (t < 0.15f) {
					intensity = t / 0.15f;
				} else {
					intensity = 1f - (t - 0.15f) / 0.85f;
				}

				_flinchPitchOffset = targetPitchKick * intensity;
				_flinchRollOffset = targetRollKick * intensity;

				yield return null;
			}

			_flinchPitchOffset = 0f;
			_flinchRollOffset = 0f;
		}

		private void Awake() {
			// get a reference to our main camera
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
			instance = this;
		}

		private void Start() {
			perlinOffset1 = new Vector2(Random.value * 1000f, Random.value * 1000f);
			perlinOffset2 = new Vector2(Random.value * 1000f, Random.value * 1000f);
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
			_playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

			// reset our timeouts on start
			_jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;
		}

		private void Update()
		{
			time += Time.deltaTime * 2;
			JumpAndGravity();
			GroundedCheck();
			Move();

			if (Input.GetKeyDown(KeyCode.F) && !smoking) {
				StartCoroutine(SmokeRoutine());
			}

			if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) {
				TryShoot();
			}

			if (Input.GetKeyDown(KeyCode.R) && !smoking && !reloading) {
				TryReload();
			}
		}

		private int bulletsInGun = 9;
		private int bulletsTotal = 9;
		private float reloadTime = 1.5f;

		public void TryShoot() {
			if (!reloading && !smoking) {
				if (bulletsInGun <= 0) {
					if (audioSource != null) {
						if (dryFiringSound != null) {
							audioSource.PlayOneShot(dryFiringSound);
						}
					}
				} else {
					bulletsInGun--;
					if (audioSource != null) {
						if (gunshotSound != null) {
							audioSource.PlayOneShot(gunshotSound);
						}
					}
					if (_flinchCoroutine != null) {
						StopCoroutine(_flinchCoroutine);
					}
					_flinchCoroutine = StartCoroutine(FlinchEffect());

					// Raycast to check for enemy hit
					if (_mainCamera != null) {
						Ray ray = new Ray(_mainCamera.transform.position, _mainCamera.transform.forward);
						RaycastHit hit;
						if (Physics.Raycast(ray, out hit)) {
							if (hit.collider.CompareTag("Enemy")) {
								StationaryBandit bandit = hit.collider.GetComponent<StationaryBandit>();
								if (bandit == null) {
									bandit = hit.collider.GetComponentInParent<StationaryBandit>();
								}
								if (bandit != null) {
									bandit.health--;
									Debug.Log("Hit Enemy! Enemy health: " + bandit.health);
									if (bandit.health <= 0) {
										Destroy(bandit.gameObject);
										enemies--;
										updateText();
									}
								}
							}
						}
					}
				}
			}
		}

		public void TryReload() {
			if (!reloading && !smoking) {
				if (bulletsInGun < 9) {
					StartCoroutine(ReloadRoutine());
				}
			}
		}

		IEnumerator ReloadRoutine() {
			reloading = true;

			// 1. Play the reloading sound effect
			if (audioSource != null && reloadSound != null) {
				audioSource.PlayOneShot(reloadSound);
			}

			// 2. Visually tilt the gun down slightly to show we are reloading
			float elapsed = 0f;
			Quaternion idleRotation = Quaternion.identity;
			Quaternion reloadRotation = Quaternion.Euler(30f, 0f, 0f);
			Vector3 originalPosition = gunTransform.localPosition;
			Vector3 reloadPosition = originalPosition - new Vector3(-0.1f, 0.15f, 0.1f);

			while (elapsed < 1f) {
				elapsed += Time.deltaTime * (1f / (reloadTime * 0.3f)); // Tilt down quickly (30% of reload time)
				gunTransform.localRotation = Quaternion.Slerp(idleRotation, reloadRotation, elapsed);
				gunTransform.localPosition = Vector3.Lerp(originalPosition, reloadPosition, elapsed);
				yield return null;
			}

			// 3. Wait in the tilted position (simulating putting bullets in)
			yield return new WaitForSeconds(2.8f);

			// 4. Snap the gun back up to ready position
			elapsed = 0f;
			while (elapsed < 1f) {
				elapsed += Time.deltaTime * (1f / (reloadTime * 0.3f)); // Snap back up quickly (30% of reload time)
				gunTransform.localRotation = Quaternion.Slerp(reloadRotation, idleRotation, elapsed);
				gunTransform.localPosition = Vector3.Lerp(reloadPosition, originalPosition, elapsed);
				yield return null;
			}

			// Ensure rotation is perfectly reset to default
			gunTransform.localRotation = idleRotation;

			bulletsInGun = bulletsTotal;
			reloading = false;
		}

		private void LateUpdate()
		{
			CameraRotation();
		}

		private void GroundedCheck() {
			// set sphere position, with offset
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
		}

		private void CameraRotation() {
			// if there is an input
			if (_input.look.sqrMagnitude >= _threshold)
			{
				//Don't multiply mouse input by Time.deltaTime
				float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

				_cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
				_rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

				// clamp our pitch rotation
				_cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

				// rotate the player left and right
				transform.Rotate(Vector3.up * _rotationVelocity);
			}

			// Update Cinemachine camera target pitch and roll (with flinch offsets)
			CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch + _flinchPitchOffset, 0.0f, (_cameraRoll * Drunkenness) + _flinchRollOffset);
		}

		private void Move() {
			// set target speed based on move speed, sprint speed and if sprint is pressed
			float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

			// a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

			// note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
			// if there is no input, set the target speed to 0
			if (_input.move == Vector2.zero) targetSpeed = 0.0f;

			// a reference to the players current horizontal velocity
			float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

			float speedOffset = 0.1f;
			float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

			// accelerate or decelerate to target speed
			if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
			{
				// creates curved result rather than a linear one giving a more organic speed change
				// note T in Lerp is clamped, so we don't need to clamp our speed
				_speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);

				// round speed to 3 decimal places
				_speed = Mathf.Round(_speed * 1000f) / 1000f;
			}
			else
			{
				_speed = targetSpeed;
			}

			// normalise input direction
			Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

			// note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
			// if there is a move input rotate player when the player is moving
			if (_input.move != Vector2.zero)
			{
				// move
				inputDirection = transform.right * _input.move.x + transform.forward * _input.move.y;
			}

			float xOffset = (Mathf.PerlinNoise(perlinOffset1.x, perlinOffset1.y + time) - 0.5f) * 2f;
			float yOffset = (Mathf.PerlinNoise(perlinOffset2.x, perlinOffset2.y + time) - 0.5f) * 2f;
			xOffset = Mathf.Pow(Mathf.Abs(xOffset), 2) * Mathf.Sign(xOffset);
			yOffset = Mathf.Pow(Mathf.Abs(yOffset), 2) * Mathf.Sign(yOffset);
			Vector3 drunkenMovement = new Vector3(xOffset, 0.0f, yOffset) * Drunkenness;

			// move the player
			_controller.Move(drunkenMovement * Time.deltaTime + inputDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

			// Calculate lateral speed relative to the player's facing direction
			float lateralSpeed = Vector3.Dot(drunkenMovement + inputDirection.normalized * _speed, transform.right);

			// Target roll: lean camera into the direction of motion (e.g. moving right -> lean right / negative Z rotation)
			float targetRoll = -lateralSpeed * LeanIntensity;
			_cameraRoll = Mathf.Lerp(_cameraRoll, targetRoll, Time.deltaTime * LeanSmoothSpeed);

			speedCounter += (drunkenMovement * Time.deltaTime + inputDirection.normalized * (_speed * Time.deltaTime)).magnitude;
			if (speedCounter > 1.8f) {
				audioSource.PlayOneShot(stepSounds[Random.Range(0, stepSounds.Length)]);
				// Play audio faster if we're stepping quickly (drunk)
				if ((drunkenMovement + inputDirection.normalized).magnitude > 1.25f) {
					speedCounter -= 0.9f;
				} else {
					speedCounter -= 1.8f;
				}
			}
		}

		float speedCounter = 0;

		private void JumpAndGravity()
		{
			if (Grounded)
			{
				// reset the fall timeout timer
				_fallTimeoutDelta = FallTimeout;

				// stop our velocity dropping infinitely when grounded
				if (_verticalVelocity < 0.0f)
				{
					_verticalVelocity = -2f;
				}

				// Jump
				if (_input.jump && _jumpTimeoutDelta <= 0.0f)
				{
					// the square root of H * -2 * G = how much velocity needed to reach desired height
					_verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
				}

				// jump timeout
				if (_jumpTimeoutDelta >= 0.0f)
				{
					_jumpTimeoutDelta -= Time.deltaTime;
				}
			}
			else
			{
				// reset the jump timeout timer
				_jumpTimeoutDelta = JumpTimeout;

				// fall timeout
				if (_fallTimeoutDelta >= 0.0f)
				{
					_fallTimeoutDelta -= Time.deltaTime;
				}

				// if we are not grounded, do not jump
				_input.jump = false;
			}

			// apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
			if (_verticalVelocity < _terminalVelocity)
			{
				_verticalVelocity += Gravity * Time.deltaTime;
			}
		}

		private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}

		float animationSpeed = 1;

		IEnumerator SmokeRoutine() {
			smoking = true;

			// Move cigarette UP
			float elapsed = 0f;
			Quaternion idleRotation = Quaternion.identity;
			Quaternion reloadRotation = Quaternion.Euler(30f, 0f, 0f);
			Vector3 originalPosition = gunTransform.localPosition;
			Vector3 reloadPosition = originalPosition - new Vector3(-0.1f, 0.15f, 0.1f);
			while (elapsed < 1f)
			{
				elapsed += Time.deltaTime * animationSpeed;
				cigarette.transform.localPosition = Vector3.Lerp(new Vector3(0, -0.34f, 0.446f), new Vector3(0, -0.14f, 0.446f), elapsed);
				gunTransform.localRotation = Quaternion.Slerp(idleRotation, reloadRotation, elapsed);
				gunTransform.localPosition = Vector3.Lerp(originalPosition, reloadPosition, elapsed);
				yield return null;
			}

			cigarette.transform.localPosition = new Vector3(0, -0.14f, 0.446f);

			if (audioSource != null && smokeSound != null) {
				if (smokeSoundLong != null && Drunkenness >= 1) {
					audioSource.PlayOneShot(smokeSoundLong);
				} else {
					audioSource.PlayOneShot(smokeSound);
				}
			}

			yield return new WaitForSeconds(1.8f);

			// Move cigarette DOWN
			elapsed = 0f;
			while (elapsed < 1f)
			{
				elapsed += Time.deltaTime * animationSpeed;
				cigarette.transform.localPosition = Vector3.Lerp(new Vector3(0, -0.14f, 0.446f), new Vector3(0, -0.34f, 0.446f), elapsed);
				transform.Rotate(-Vector3.right * Time.deltaTime * 20);
				yield return null;
			}

			if (smokeParticles != null) {
				smokeParticles.Play();
			}

			cigarette.transform.localPosition = new Vector3(0, -0.34f, 0.446f);
			elapsed = 0f;
			while (elapsed < 0.8f)
			{
				elapsed += Time.deltaTime * animationSpeed;
				health += Time.deltaTime * 100;
				if (health > 100) {
					health = 100;
				}
				updateHealthUi();
				yield return null;
			}
			elapsed = 0f;
			while (elapsed < 1f)
			{
				elapsed += Time.deltaTime * animationSpeed;
				transform.Rotate(Vector3.right * Time.deltaTime * 20);
				health += Time.deltaTime * 100;
				if (health > 100) {
					health = 100;
				}
				updateHealthUi();
				gunTransform.localRotation = Quaternion.Slerp(reloadRotation, idleRotation, elapsed);
				gunTransform.localPosition = Vector3.Lerp(reloadPosition, originalPosition, elapsed);
				yield return null;
			}

			//health += healAmount;
			//health = Mathf.Clamp(health, 0, maxHealth);

			gunTransform.localRotation = idleRotation;
			Drunkenness += 0.2f;
			Debug.Log("Drunkenness: " + Drunkenness);
			audioSource.pitch = 1 - 0.5f/(1+Drunkenness);
			cigs--;
			updateText();

			//intoxication += intoxicationAmount;
			//intoxication = Mathf.Clamp(intoxication, 0, maxIntoxication);

			//Debug.Log("Smoked! Health: " + health + " | Intoxication: " + intoxication);
			smoking = false;
		}
	}
}
