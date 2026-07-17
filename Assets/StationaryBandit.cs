using UnityEngine;
using StarterAssets;

public class StationaryBandit : MonoBehaviour
{
	[Header("Targeting")]
	private Transform player;
	//private PlayerGameplay playerStats;

	[Header("Combat Settings")]
	public float shootingRange = 15f;
	public float fireRate = 2f; // Seconds between shots
	public float damage = 15f;
	
	[Header("Effects")]
	public GameObject muzzleFlashPrefab; // Optional visual flash
	public AudioSource audioSource;
	public AudioClip shootSound;

	private float lastShotTime = 0f;

	public int health = 1;

	void Start()
	{
		// Automatically find the player using their Tag (makes spawning multiple bandits easy!)
		GameObject playerObj = GameObject.FindWithTag("Player");
		if (playerObj != null)
		{
			player = playerObj.transform;
			//playerStats = playerObj.GetComponent<PlayerGameplay>();
		}

		// Try to automatically get the AudioSource component on this object
		if (audioSource == null)
		{
			audioSource = GetComponent<AudioSource>();
		}
	}

	void Update()
	{
		//if (player == null || playerStats == null) return;
		if (player == null) {
			Debug.Log("Can't find player");
			return;
		}

		// 1. Calculate distance to player
		float distanceToPlayer = Vector3.Distance(transform.position, player.position);

		// 2. If player is in range, rotate to face them and shoot on a timer
		if (distanceToPlayer <= shootingRange) {
			AimAtPlayer();

			if (Time.time >= lastShotTime + fireRate) {
				// Check if we have a clear line of sight (not shooting through saloon/canyon walls)
				if (HasLineOfSight()) {
					ShootPlayer();
				}
			}
		}
	}

	void AimAtPlayer()
	{
		// Rotate the bandit to face the player, but ignore the Y axis so they stay standing upright
		Vector3 targetDirection = player.position - transform.position;
		targetDirection.y = 0; 
		
		if (targetDirection != Vector3.zero)
		{
			transform.rotation = Quaternion.LookRotation(targetDirection);
		}
	}

	bool HasLineOfSight() {
		RaycastHit hit;
		
		// Aim for the player's chest (slightly above their root position)
		Vector3 targetPoint = player.position;
		Vector3 direction = player.position - transform.position;

		// Fire a raycast from the bandit's chest level
		Vector3 startPoint = transform.position + Vector3.up * 1.2f;

		if (Physics.Raycast(transform.position, player.position - transform.position, out hit, shootingRange))
		{
			// If the first solid object we hit is the Player, we have a clear line of sight
			if (hit.collider.CompareTag("Player"))
			{
				return true;
			}
		}

		return false;
	}

	void ShootPlayer() {
		lastShotTime = Time.time;

		// Play the gunshot audio
		if (audioSource != null && shootSound != null) {
			audioSource.PlayOneShot(shootSound);
		}

		// Spawn visual muzzle flash slightly in front of the bandit
		if (muzzleFlashPrefab != null) {
			Vector3 spawnPosition = transform.position + transform.forward + Vector3.up * 1.2f;
			GameObject flash = Instantiate(muzzleFlashPrefab, spawnPosition, transform.rotation);
			Destroy(flash, 0.5f); // Automatically cleanup the flash after 0.5 seconds
		}

		// Deal damage directly to your player's health script
		//playerStats.TakeDamage(damage);
		Debug.Log(gameObject.name + " shot the player!");
		if (FirstPersonController.instance != null) {
			FirstPersonController.instance.takeDamage();
		}
	}
}
