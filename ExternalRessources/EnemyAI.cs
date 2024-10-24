// Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// EnemyAI
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public abstract class EnemyAI : NetworkBehaviour
{
	public EnemyType enemyType;

	[Space(5f)]
	public SkinnedMeshRenderer[] skinnedMeshRenderers;

	public MeshRenderer[] meshRenderers;

	public Animator creatureAnimator;

	public AudioSource creatureVoice;

	public AudioSource creatureSFX;

	public Transform eye;

	public AudioClip dieSFX;

	[Space(3f)]
	public EnemyBehaviourState[] enemyBehaviourStates;

	public EnemyBehaviourState currentBehaviourState;

	public int currentBehaviourStateIndex;

	public int previousBehaviourStateIndex;

	public int currentOwnershipOnThisClient = -1;

	public bool isInsidePlayerShip;

	[Header("AI Calculation / Netcode")]
	public float AIIntervalTime = 0.2f;

	public bool inSpecialAnimation;

	public PlayerControllerB inSpecialAnimationWithPlayer;

	[HideInInspector]
	public Vector3 serverPosition;

	[HideInInspector]
	public Vector3 serverRotation;

	private float previousYRotation;

	private float targetYRotation;

	public NavMeshAgent agent;

	[HideInInspector]
	public NavMeshPath path1;

	public GameObject[] allAINodes;

	public Transform targetNode;

	public Transform favoriteSpot;

	[HideInInspector]
	public float tempDist;

	[HideInInspector]
	public float mostOptimalDistance;

	[HideInInspector]
	public float pathDistance;

	[HideInInspector]
	public NetworkObject thisNetworkObject;

	public int thisEnemyIndex;

	public bool isClientCalculatingAI;

	public float updatePositionThreshold = 1f;

	private Vector3 tempVelocity;

	public PlayerControllerB targetPlayer;

	public bool movingTowardsTargetPlayer;

	public bool moveTowardsDestination = true;

	public Vector3 destination;

	public float addPlayerVelocityToDestination;

	private float updateDestinationInterval;

	public float syncMovementSpeed = 0.22f;

	public float timeSinceSpawn;

	public float exitVentAnimationTime = 1f;

	public bool ventAnimationFinished;

	[Space(5f)]
	public bool isEnemyDead;

	public bool daytimeEnemyLeaving;

	public int enemyHP = 3;

	public GameObject[] nodesTempArray;

	public float openDoorSpeedMultiplier;

	public bool useSecondaryAudiosOnAnimatedObjects;

	public AISearchRoutine currentSearch;

	public Coroutine searchCoroutine;

	public Coroutine chooseTargetNodeCoroutine;

	private RaycastHit raycastHit;

	private Ray LOSRay;

	public bool DebugEnemy;

	public int stunnedIndefinitely;

	public float stunNormalizedTimer;

	public float postStunInvincibilityTimer;

	public PlayerControllerB stunnedByPlayer;

	private float setDestinationToPlayerInterval;

	public bool debugEnemyAI;

	private bool removedPowerLevel;

	public bool isOutside;

	public bool hitsPhysicsObjects;

	private System.Random searchRoutineRandom;

	private int getFarthestNodeAsyncBookmark;

	public bool gotFarthestNodeAsync;

	private Collider[] overlapColliders;

	public virtual void SetEnemyStunned(bool setToStunned, float setToStunTime = 1f, PlayerControllerB setStunnedByPlayer = null)
	{
		if (isEnemyDead || !enemyType.canBeStunned)
		{
			return;
		}
		if (setToStunned)
		{
			if (!(postStunInvincibilityTimer >= 0f))
			{
				if (stunNormalizedTimer <= 0f && creatureVoice != null)
				{
					creatureVoice.PlayOneShot(enemyType.stunSFX);
				}
				stunnedByPlayer = setStunnedByPlayer;
				postStunInvincibilityTimer = 0.5f;
				stunNormalizedTimer = setToStunTime;
			}
		}
		else
		{
			stunnedByPlayer = null;
			if (stunNormalizedTimer > 0f)
			{
				stunNormalizedTimer = 0f;
			}
		}
	}

	public virtual void UseNestSpawnObject(EnemyAINestSpawnObject nestSpawnObject)
	{
		agent.enabled = false;
		base.transform.position = nestSpawnObject.transform.position;
		base.transform.rotation = nestSpawnObject.transform.rotation;
		agent.enabled = true;
		if (RoundManager.Instance.enemyNestSpawnObjects.Contains(nestSpawnObject))
		{
			RoundManager.Instance.enemyNestSpawnObjects.Remove(nestSpawnObject);
		}
		if (!enemyType.useMinEnemyThresholdForNest)
		{
			Debug.Log($"Enemy {base.gameObject.name} #{thisEnemyIndex} destroying nest object '{nestSpawnObject.gameObject}'");
			UnityEngine.Object.Destroy(nestSpawnObject.gameObject);
		}
	}

	public virtual void Start()
	{
		try
		{
			overlapColliders = new Collider[1];
			agent = base.gameObject.GetComponentInChildren<NavMeshAgent>();
			skinnedMeshRenderers = base.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
			meshRenderers = base.gameObject.GetComponentsInChildren<MeshRenderer>();
			if (creatureAnimator == null)
			{
				creatureAnimator = base.gameObject.GetComponentInChildren<Animator>();
			}
			thisNetworkObject = base.gameObject.GetComponentInChildren<NetworkObject>();
			thisEnemyIndex = RoundManager.Instance.numberOfEnemiesInScene;
			RoundManager.Instance.numberOfEnemiesInScene++;
			isOutside = enemyType.isOutsideEnemy;
			if (enemyType.isOutsideEnemy)
			{
				allAINodes = GameObject.FindGameObjectsWithTag("OutsideAINode");
				if (enemyType.nestSpawnPrefab != null)
				{
					for (int i = 0; i < RoundManager.Instance.enemyNestSpawnObjects.Count; i++)
					{
						if (RoundManager.Instance.enemyNestSpawnObjects[i] == null)
						{
							RoundManager.Instance.enemyNestSpawnObjects.RemoveAt(i);
						}
						else if (RoundManager.Instance.enemyNestSpawnObjects[i].enemyType == enemyType)
						{
							UseNestSpawnObject(RoundManager.Instance.enemyNestSpawnObjects[i]);
							break;
						}
					}
				}
				if (GameNetworkManager.Instance.localPlayerController != null)
				{
					EnableEnemyMesh(!StartOfRound.Instance.hangarDoorsClosed || !GameNetworkManager.Instance.localPlayerController.isInHangarShipRoom);
				}
			}
			else
			{
				allAINodes = GameObject.FindGameObjectsWithTag("AINode");
			}
			if (!base.IsServer)
			{
				RoundManager.Instance.SpawnedEnemies.Add(this);
			}
			path1 = new NavMeshPath();
			openDoorSpeedMultiplier = enemyType.doorSpeedMultiplier;
			serverPosition = base.transform.position;
			if (base.IsOwner)
			{
				SyncPositionToClients();
			}
			else
			{
				SetClientCalculatingAI(enable: false);
			}
		}
		catch (Exception arg)
		{
			Debug.LogError($"Error when initializing enemy variables for {base.gameObject.name} : {arg}");
		}
	}

	public PlayerControllerB MeetsStandardPlayerCollisionConditions(Collider other, bool inKillAnimation = false, bool overrideIsInsideFactoryCheck = false)
	{
		if (isEnemyDead)
		{
			return null;
		}
		if (!ventAnimationFinished)
		{
			return null;
		}
		if (inKillAnimation)
		{
			return null;
		}
		if (stunNormalizedTimer >= 0f)
		{
			return null;
		}
		PlayerControllerB component = other.gameObject.GetComponent<PlayerControllerB>();
		if (component == null || component != GameNetworkManager.Instance.localPlayerController)
		{
			return null;
		}
		if (!PlayerIsTargetable(component, cannotBeInShip: false, overrideIsInsideFactoryCheck))
		{
			Debug.Log("Player is not targetable");
			return null;
		}
		return component;
	}

	public virtual void OnCollideWithPlayer(Collider other)
	{
		if (debugEnemyAI)
		{
			Debug.Log(base.gameObject.name + ": Collided with player!");
		}
	}

	public virtual void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null)
	{
		if (base.IsServer && debugEnemyAI)
		{
			Debug.Log(base.gameObject.name + " collided with enemy!: " + other.gameObject.name);
		}
	}

	public void SwitchToBehaviourState(int stateIndex)
	{
		SwitchToBehaviourStateOnLocalClient(stateIndex);
		SwitchToBehaviourServerRpc(stateIndex);
	}

	[ServerRpc(RequireOwnership = false)]
	public void SwitchToBehaviourServerRpc(int stateIndex)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
			{
				ServerRpcParams serverRpcParams = default(ServerRpcParams);
				FastBufferWriter bufferWriter = __beginSendServerRpc(2081148948u, serverRpcParams, RpcDelivery.Reliable);
				BytePacker.WriteValueBitPacked(bufferWriter, stateIndex);
				__endSendServerRpc(ref bufferWriter, 2081148948u, serverRpcParams, RpcDelivery.Reliable);
			}
			if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost) && base.NetworkObject.IsSpawned)
			{
				SwitchToBehaviourClientRpc(stateIndex);
			}
		}
	}

	[ClientRpc]
	public void SwitchToBehaviourClientRpc(int stateIndex)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
			{
				ClientRpcParams clientRpcParams = default(ClientRpcParams);
				FastBufferWriter bufferWriter = __beginSendClientRpc(2962895088u, clientRpcParams, RpcDelivery.Reliable);
				BytePacker.WriteValueBitPacked(bufferWriter, stateIndex);
				__endSendClientRpc(ref bufferWriter, 2962895088u, clientRpcParams, RpcDelivery.Reliable);
			}
			if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost) && stateIndex != currentBehaviourStateIndex)
			{
				SwitchToBehaviourStateOnLocalClient(stateIndex);
			}
		}
	}

	public void SwitchToBehaviourStateOnLocalClient(int stateIndex)
	{
		if (currentBehaviourStateIndex != stateIndex)
		{
			previousBehaviourStateIndex = currentBehaviourStateIndex;
			currentBehaviourStateIndex = stateIndex;
			currentBehaviourState = enemyBehaviourStates[stateIndex];
			PlayAudioOfCurrentState();
			PlayAnimationOfCurrentState();
		}
	}

	public void PlayAnimationOfCurrentState()
	{
		if (!(creatureAnimator == null))
		{
			if (currentBehaviourState.IsAnimTrigger)
			{
				creatureAnimator.SetTrigger(currentBehaviourState.parameterString);
			}
			else
			{
				creatureAnimator.SetBool(currentBehaviourState.parameterString, currentBehaviourState.boolValue);
			}
		}
	}

	public void PlayAudioOfCurrentState()
	{
		if ((bool)creatureVoice)
		{
			if (currentBehaviourState.playOneShotVoice)
			{
				creatureVoice.PlayOneShot(currentBehaviourState.VoiceClip);
				WalkieTalkie.TransmitOneShotAudio(creatureVoice, currentBehaviourState.VoiceClip, creatureVoice.volume);
			}
			else if (currentBehaviourState.VoiceClip != null)
			{
				creatureVoice.clip = currentBehaviourState.VoiceClip;
				creatureVoice.Play();
			}
		}
		if ((bool)creatureSFX)
		{
			if (currentBehaviourState.playOneShotSFX)
			{
				creatureSFX.PlayOneShot(currentBehaviourState.SFXClip);
				WalkieTalkie.TransmitOneShotAudio(creatureSFX, currentBehaviourState.SFXClip, creatureSFX.volume);
			}
			else if (currentBehaviourState.SFXClip != null)
			{
				creatureSFX.clip = currentBehaviourState.SFXClip;
				creatureSFX.Play();
			}
		}
	}

	public void SetMovingTowardsTargetPlayer(PlayerControllerB playerScript)
	{
		movingTowardsTargetPlayer = true;
		targetPlayer = playerScript;
	}

	public bool SetDestinationToPosition(Vector3 position, bool checkForPath = false)
	{
		if (checkForPath)
		{
			position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 1.75f);
			path1 = new NavMeshPath();
			if (!agent.CalculatePath(position, path1))
			{
				return false;
			}
			if (Vector3.Distance(path1.corners[path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 2.7f)) > 1.55f)
			{
				return false;
			}
		}
		moveTowardsDestination = true;
		movingTowardsTargetPlayer = false;
		destination = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, -1f);
		return true;
	}

	public virtual void DoAIInterval()
	{
		if (moveTowardsDestination)
		{
			agent.SetDestination(destination);
		}
		SyncPositionToClients();
	}

	public void SyncPositionToClients()
	{
		if (Vector3.Distance(serverPosition, base.transform.position) > updatePositionThreshold)
		{
			serverPosition = base.transform.position;
			if (base.IsServer)
			{
				UpdateEnemyPositionClientRpc(serverPosition);
			}
			else
			{
				UpdateEnemyPositionServerRpc(serverPosition);
			}
		}
	}

	public PlayerControllerB CheckLineOfSightForPlayer(float width = 45f, int range = 60, int proximityAwareness = -1)
	{
		if (isOutside && !enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
		{
			range = Mathf.Clamp(range, 0, 30);
		}
		for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
		{
			Vector3 position = StartOfRound.Instance.allPlayerScripts[i].gameplayCamera.transform.position;
			if (Vector3.Distance(position, eye.position) < (float)range && !Physics.Linecast(eye.position, position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
			{
				Vector3 to = position - eye.position;
				if (Vector3.Angle(eye.forward, to) < width || (proximityAwareness != -1 && Vector3.Distance(eye.position, position) < (float)proximityAwareness))
				{
					return StartOfRound.Instance.allPlayerScripts[i];
				}
			}
		}
		return null;
	}

	public PlayerControllerB CheckLineOfSightForClosestPlayer(float width = 45f, int range = 60, int proximityAwareness = -1, float bufferDistance = 0f)
	{
		if (isOutside && !enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
		{
			range = Mathf.Clamp(range, 0, 30);
		}
		float num = 1000f;
		float num2 = 1000f;
		int num3 = -1;
		for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
		{
			Vector3 position = StartOfRound.Instance.allPlayerScripts[i].gameplayCamera.transform.position;
			if (DebugEnemy)
			{
				Debug.DrawLine(eye.position, position, Color.green, AIIntervalTime);
			}
			if (!Physics.Linecast(eye.position, position, out raycastHit, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
			{
				Vector3 to = position - eye.position;
				num = Vector3.Distance(eye.position, position);
				if ((Vector3.Angle(eye.forward, to) < width || (proximityAwareness != -1 && num < (float)proximityAwareness)) && num < num2)
				{
					num2 = num;
					num3 = i;
				}
			}
			else if (DebugEnemy)
			{
				Debug.Log($"{enemyType.enemyName} #{thisEnemyIndex}: LOS check for player #{i} hit an object: {raycastHit.point}, {raycastHit.collider.gameObject.name}, {raycastHit.collider.transform.gameObject.name}; {raycastHit.collider.name}", raycastHit.collider.gameObject);
			}
		}
		if (targetPlayer != null && num3 != -1 && targetPlayer != StartOfRound.Instance.allPlayerScripts[num3] && bufferDistance > 0f && Mathf.Abs(num2 - Vector3.Distance(base.transform.position, targetPlayer.transform.position)) < bufferDistance)
		{
			return null;
		}
		if (num3 < 0)
		{
			return null;
		}
		mostOptimalDistance = num2;
		return StartOfRound.Instance.allPlayerScripts[num3];
	}

	public PlayerControllerB[] GetAllPlayersInLineOfSight(float width = 45f, int range = 60, Transform eyeObject = null, float proximityCheck = -1f, int layerMask = -1)
	{
		if (layerMask == -1)
		{
			layerMask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;
		}
		if (eyeObject == null)
		{
			eyeObject = eye;
		}
		if (isOutside && !enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
		{
			range = Mathf.Clamp(range, 0, 30);
		}
		List<PlayerControllerB> list = new List<PlayerControllerB>(4);
		for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
		{
			if (!PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i]))
			{
				continue;
			}
			Vector3 position = StartOfRound.Instance.allPlayerScripts[i].gameplayCamera.transform.position;
			if (Vector3.Distance(eye.position, position) < (float)range && !Physics.Linecast(eyeObject.position, position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
			{
				Vector3 to = position - eyeObject.position;
				if (Vector3.Angle(eyeObject.forward, to) < width || Vector3.Distance(base.transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position) < proximityCheck)
				{
					list.Add(StartOfRound.Instance.allPlayerScripts[i]);
				}
			}
		}
		if (list.Count == 4)
		{
			return StartOfRound.Instance.allPlayerScripts;
		}
		if (list.Count > 0)
		{
			return list.ToArray();
		}
		return null;
	}

	public bool CheckLineOfSightForPosition(Vector3 objectPosition, float width = 45f, int range = 60, float proximityAwareness = -1f, Transform overrideEye = null)
	{
		if (!isOutside)
		{
			if (objectPosition.y > -80f)
			{
				return false;
			}
		}
		else if (objectPosition.y < -100f)
		{
			return false;
		}
		Transform transform = ((overrideEye != null) ? overrideEye : ((!(eye == null)) ? eye : base.transform));
		if (Vector3.Distance(transform.position, objectPosition) < (float)range && !Physics.Linecast(transform.position, objectPosition, out var _, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
		{
			Vector3 to = objectPosition - transform.position;
			if (debugEnemyAI)
			{
				Debug.DrawRay(transform.position, objectPosition - transform.position, Color.green, 2f);
			}
			if (Vector3.Angle(transform.forward, to) < width || Vector3.Distance(base.transform.position, objectPosition) < proximityAwareness)
			{
				return true;
			}
		}
		return false;
	}

	public GameObject CheckLineOfSight(List<GameObject> objectsToLookFor, float width = 45f, int range = 60, float proximityAwareness = -1f, Transform useEye = null, int[] itemIdExceptions = null)
	{
		if (useEye == null)
		{
			useEye = eye;
		}
		GrabbableObject component = base.transform.GetComponent<GrabbableObject>();
		for (int i = 0; i < objectsToLookFor.Count; i++)
		{
			if (objectsToLookFor[i] == null)
			{
				objectsToLookFor.TrimExcess();
				continue;
			}
			Vector3 position = objectsToLookFor[i].transform.position;
			if (!isOutside)
			{
				if (position.y > -80f)
				{
					continue;
				}
			}
			else if (position.y < -100f)
			{
				continue;
			}
			if (!(Vector3.Distance(useEye.position, objectsToLookFor[i].transform.position) < (float)range) || Physics.Linecast(useEye.position, position + Vector3.up * 0.05f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
			{
				continue;
			}
			Vector3 to = position - useEye.position;
			if (!(Vector3.Angle(useEye.forward, to) < width) && !(Vector3.Distance(base.transform.position, position) < proximityAwareness))
			{
				continue;
			}
			if (itemIdExceptions != null)
			{
				GrabbableObject component2 = objectsToLookFor[i].GetComponent<GrabbableObject>();
				if (!(component2 != null) || !(component2 != component) || component2.isHeld || component2.deactivated)
				{
					continue;
					continue;
				}
				for (int j = 0; j < itemIdExceptions.Length; j++)
				{
					_ = component2.itemProperties.itemId;
					_ = itemIdExceptions[j];
				}
			}
			return objectsToLookFor[i];
		}
		return null;
	}

	public void StartSearch(Vector3 startOfSearch, AISearchRoutine newSearch = null)
	{
		StopSearch(currentSearch);
		movingTowardsTargetPlayer = false;
		if (newSearch == null)
		{
			currentSearch = new AISearchRoutine();
			newSearch = currentSearch;
		}
		else
		{
			currentSearch = newSearch;
		}
		currentSearch.currentSearchStartPosition = startOfSearch;
		currentSearch.startedSearchAtSelf = Vector3.Distance(startOfSearch, base.transform.position) < 2f;
		if (currentSearch.unsearchedNodes.Count <= 0)
		{
			currentSearch.unsearchedNodes = allAINodes.ToList();
		}
		searchRoutineRandom = new System.Random(RoundUpToNearestFive(startOfSearch.x) + RoundUpToNearestFive(startOfSearch.z));
		searchCoroutine = StartCoroutine(CurrentSearchCoroutine());
		currentSearch.inProgress = true;
	}

	private int RoundUpToNearestFive(float x)
	{
		return (int)(x / 5f) * 5;
	}

	public void StopSearch(AISearchRoutine search, bool clear = true)
	{
		if (search != null)
		{
			if (searchCoroutine != null)
			{
				StopCoroutine(searchCoroutine);
			}
			if (chooseTargetNodeCoroutine != null)
			{
				StopCoroutine(chooseTargetNodeCoroutine);
			}
			search.calculatingNodeInSearch = false;
			search.inProgress = false;
			if (clear)
			{
				search.unsearchedNodes = allAINodes.ToList();
				search.timesFinishingSearch = 0;
				search.nodesEliminatedInCurrentSearch = 0;
				search.currentTargetNode = null;
				search.currentSearchStartPosition = Vector3.zero;
				search.nextTargetNode = null;
				search.choseTargetNode = false;
				search.startedSearchAtSelf = false;
			}
		}
	}

	private IEnumerator CurrentSearchCoroutine()
	{
		yield return null;
		while (searchCoroutine != null && base.IsOwner)
		{
			yield return null;
			if (currentSearch.unsearchedNodes.Count <= 0)
			{
				FinishedCurrentSearchRoutine();
				if (!currentSearch.loopSearch)
				{
					currentSearch.inProgress = false;
					searchCoroutine = null;
					yield break;
				}
				currentSearch.unsearchedNodes = allAINodes.ToList();
				currentSearch.timesFinishingSearch++;
				currentSearch.nodesEliminatedInCurrentSearch = 0;
				yield return new WaitForSeconds(1f);
			}
			if (currentSearch.choseTargetNode && currentSearch.unsearchedNodes.Contains(currentSearch.nextTargetNode))
			{
				if (debugEnemyAI)
				{
					Debug.Log($"finding next node: {currentSearch.choseTargetNode}; node already found ahead of time");
				}
				currentSearch.currentTargetNode = currentSearch.nextTargetNode;
			}
			else
			{
				if (debugEnemyAI)
				{
					Debug.Log("finding next node; calculation not finished ahead of time");
				}
				currentSearch.waitingForTargetNode = true;
				StartCalculatingNextTargetNode();
				yield return new WaitUntil(() => currentSearch.choseTargetNode);
			}
			currentSearch.waitingForTargetNode = false;
			if (currentSearch.unsearchedNodes.Count <= 0 || currentSearch.currentTargetNode == null)
			{
				continue;
			}
			if (debugEnemyAI)
			{
				int num = 0;
				for (int j = 0; j < currentSearch.unsearchedNodes.Count; j++)
				{
					if (currentSearch.unsearchedNodes[j] == currentSearch.currentTargetNode)
					{
						Debug.Log($"Found node {currentSearch.unsearchedNodes[j]} within list of unsearched nodes at index {j}");
						num++;
					}
				}
				Debug.Log($"Copies of the node {currentSearch.currentTargetNode} found in list: {num}");
				Debug.Log($"unsearched nodes contains {currentSearch.currentTargetNode}? : {currentSearch.unsearchedNodes.Contains(currentSearch.currentTargetNode)}");
				Debug.Log($"Removing {currentSearch.currentTargetNode} from unsearched nodes list with Remove()");
			}
			currentSearch.unsearchedNodes.Remove(currentSearch.currentTargetNode);
			if (debugEnemyAI)
			{
				Debug.Log($"Removed. Does list now contain {currentSearch.currentTargetNode}?: {currentSearch.unsearchedNodes.Contains(currentSearch.currentTargetNode)}");
			}
			SetDestinationToPosition(currentSearch.currentTargetNode.transform.position);
			for (int i = currentSearch.unsearchedNodes.Count - 1; i >= 0; i--)
			{
				if (Vector3.Distance(currentSearch.currentTargetNode.transform.position, currentSearch.unsearchedNodes[i].transform.position) < currentSearch.searchPrecision)
				{
					EliminateNodeFromSearch(i);
				}
				if (i % 10 == 0)
				{
					yield return null;
				}
			}
			StartCalculatingNextTargetNode();
			int timeSpent = 0;
			while (searchCoroutine != null)
			{
				if (debugEnemyAI)
				{
					Debug.Log("Current search not null");
				}
				timeSpent++;
				if (timeSpent >= 32 || (currentSearch.onlySearchNodesInLOS && Physics.Linecast(currentSearch.currentTargetNode.transform.position, currentSearch.currentSearchStartPosition, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)))
				{
					break;
				}
				yield return new WaitForSeconds(0.5f);
				if (Vector3.Distance(base.transform.position, currentSearch.currentTargetNode.transform.position) < currentSearch.searchPrecision)
				{
					if (debugEnemyAI)
					{
						Debug.Log("Enemy: Reached the target " + currentSearch.currentTargetNode.name);
					}
					ReachedNodeInSearch();
					break;
				}
				if (debugEnemyAI)
				{
					Debug.Log($"Enemy: We have not reached the target node {currentSearch.currentTargetNode.transform.name}, distance: {Vector3.Distance(base.transform.position, currentSearch.currentTargetNode.transform.position)} ; {currentSearch.searchPrecision}");
				}
			}
			if (debugEnemyAI)
			{
				Debug.Log("Reached destination node");
			}
		}
		if (!base.IsOwner)
		{
			StopSearch(currentSearch);
		}
	}

	private void StartCalculatingNextTargetNode()
	{
		if (debugEnemyAI)
		{
			Debug.Log("Calculating next target node");
			Debug.Log($"Is calculate node coroutine null? : {chooseTargetNodeCoroutine == null}; choseTargetNode: {currentSearch.choseTargetNode}");
		}
		if (chooseTargetNodeCoroutine == null)
		{
			if (debugEnemyAI)
			{
				Debug.Log("NODE A");
			}
			currentSearch.choseTargetNode = false;
			chooseTargetNodeCoroutine = StartCoroutine(ChooseNextNodeInSearchRoutine());
		}
		else if (!currentSearch.calculatingNodeInSearch)
		{
			if (debugEnemyAI)
			{
				Debug.Log("NODE B");
			}
			currentSearch.choseTargetNode = false;
			currentSearch.calculatingNodeInSearch = true;
			StopCoroutine(chooseTargetNodeCoroutine);
			chooseTargetNodeCoroutine = StartCoroutine(ChooseNextNodeInSearchRoutine());
		}
	}

	private IEnumerator ChooseNextNodeInSearchRoutine()
	{
		yield return null;
		float closestDist = 500f;
		bool gotNode = false;
		GameObject chosenNode = null;
		for (int j = 0; j < currentSearch.unsearchedNodes.Count; j++)
		{
		}
		for (int i = currentSearch.unsearchedNodes.Count - 1; i >= 0; i--)
		{
			if (!base.IsOwner)
			{
				currentSearch.calculatingNodeInSearch = false;
				yield break;
			}
			if (i % 5 == 0)
			{
				yield return null;
			}
			if (Vector3.Distance(currentSearch.currentSearchStartPosition, currentSearch.unsearchedNodes[i].transform.position) > currentSearch.searchWidth)
			{
				EliminateNodeFromSearch(i);
			}
			else if (agent.isOnNavMesh && PathIsIntersectedByLineOfSight(currentSearch.unsearchedNodes[i].transform.position, currentSearch.startedSearchAtSelf, avoidLineOfSight: false))
			{
				EliminateNodeFromSearch(i);
			}
			else if (currentSearch.onlySearchNodesInLOS && Physics.Linecast(currentSearch.unsearchedNodes[i].transform.position, currentSearch.currentSearchStartPosition, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
			{
				EliminateNodeFromSearch(i);
			}
			else
			{
				if (!currentSearch.startedSearchAtSelf)
				{
					GetPathDistance(currentSearch.unsearchedNodes[i].transform.position, currentSearch.currentSearchStartPosition);
				}
				if (pathDistance < closestDist && (!currentSearch.randomized || !gotNode || searchRoutineRandom.Next(0, 100) < 65))
				{
					closestDist = pathDistance;
					chosenNode = currentSearch.unsearchedNodes[i];
					gotNode = true;
					if (closestDist <= 0f && !currentSearch.randomized)
					{
						break;
					}
				}
			}
		}
		if (debugEnemyAI)
		{
			Debug.Log($"NODE C; chosen node: {chosenNode}");
		}
		if (currentSearch.waitingForTargetNode)
		{
			currentSearch.currentTargetNode = chosenNode;
			if (debugEnemyAI)
			{
				Debug.Log("NODE C1");
			}
		}
		else
		{
			currentSearch.nextTargetNode = chosenNode;
			if (debugEnemyAI)
			{
				Debug.Log("NODE C2");
			}
		}
		currentSearch.choseTargetNode = true;
		if (debugEnemyAI)
		{
			Debug.Log($"Chose target node?: {currentSearch.choseTargetNode} ");
		}
		currentSearch.calculatingNodeInSearch = false;
		chooseTargetNodeCoroutine = null;
	}

	public virtual void ReachedNodeInSearch()
	{
	}

	private void EliminateNodeFromSearch(GameObject node)
	{
		currentSearch.unsearchedNodes.Remove(node);
		currentSearch.nodesEliminatedInCurrentSearch++;
	}

	private void EliminateNodeFromSearch(int index)
	{
		currentSearch.unsearchedNodes.RemoveAt(index);
		currentSearch.nodesEliminatedInCurrentSearch++;
	}

	public virtual void FinishedCurrentSearchRoutine()
	{
	}

	public bool TargetClosestPlayer(float bufferDistance = 1.5f, bool requireLineOfSight = false, float viewWidth = 70f)
	{
		mostOptimalDistance = 2000f;
		PlayerControllerB playerControllerB = targetPlayer;
		targetPlayer = null;
		for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
		{
			if (PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i]) && !PathIsIntersectedByLineOfSight(StartOfRound.Instance.allPlayerScripts[i].transform.position, calculatePathDistance: false, avoidLineOfSight: false) && (!requireLineOfSight || CheckLineOfSightForPosition(StartOfRound.Instance.allPlayerScripts[i].gameplayCamera.transform.position, viewWidth, 40)))
			{
				tempDist = Vector3.Distance(base.transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
				if (tempDist < mostOptimalDistance)
				{
					mostOptimalDistance = tempDist;
					targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
				}
			}
		}
		if (targetPlayer != null && bufferDistance > 0f && playerControllerB != null && Mathf.Abs(mostOptimalDistance - Vector3.Distance(base.transform.position, playerControllerB.transform.position)) < bufferDistance)
		{
			targetPlayer = playerControllerB;
		}
		return targetPlayer != null;
	}

	public PlayerControllerB GetClosestPlayer(bool requireLineOfSight = false, bool cannotBeInShip = false, bool cannotBeNearShip = false)
	{
		PlayerControllerB result = null;
		mostOptimalDistance = 2000f;
		for (int i = 0; i < 4; i++)
		{
			if (!PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i], cannotBeInShip))
			{
				continue;
			}
			if (cannotBeNearShip)
			{
				if (StartOfRound.Instance.allPlayerScripts[i].isInElevator)
				{
					continue;
				}
				bool flag = false;
				for (int j = 0; j < RoundManager.Instance.spawnDenialPoints.Length; j++)
				{
					if (Vector3.Distance(RoundManager.Instance.spawnDenialPoints[j].transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position) < 10f)
					{
						flag = true;
						break;
					}
				}
				if (flag)
				{
					continue;
				}
			}
			if (!requireLineOfSight || !Physics.Linecast(base.transform.position + Vector3.up * 0.25f, StartOfRound.Instance.allPlayerScripts[i].transform.position, 256))
			{
				tempDist = Vector3.Distance(base.transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
				if (tempDist < mostOptimalDistance)
				{
					mostOptimalDistance = tempDist;
					result = StartOfRound.Instance.allPlayerScripts[i];
				}
			}
		}
		return result;
	}

	public bool PlayerIsTargetable(PlayerControllerB playerScript, bool cannotBeInShip = false, bool overrideInsideFactoryCheck = false)
	{
		if (cannotBeInShip && playerScript.isInHangarShipRoom)
		{
			Debug.Log("Targetable A");
			return false;
		}
		if (playerScript.isPlayerControlled && !playerScript.isPlayerDead && playerScript.inAnimationWithEnemy == null && (overrideInsideFactoryCheck || playerScript.isInsideFactory != isOutside) && playerScript.sinkingValue < 0.73f)
		{
			if (isOutside && StartOfRound.Instance.hangarDoorsClosed)
			{
				return playerScript.isInHangarShipRoom == isInsidePlayerShip;
			}
			return true;
		}
		return false;
	}

	public Transform ChooseFarthestNodeFromPosition(Vector3 pos, bool avoidLineOfSight = false, int offset = 0, bool doAsync = false, int maxAsyncIterations = 50, bool capDistance = false)
	{
		if (!doAsync || gotFarthestNodeAsync || getFarthestNodeAsyncBookmark <= 0 || nodesTempArray == null || nodesTempArray.Length == 0)
		{
			nodesTempArray = allAINodes.OrderByDescending((GameObject x) => Vector3.Distance(pos, x.transform.position)).ToArray();
		}
		Transform result = nodesTempArray[0].transform;
		int num = 0;
		if (doAsync)
		{
			if (getFarthestNodeAsyncBookmark >= nodesTempArray.Length)
			{
				getFarthestNodeAsyncBookmark = 0;
			}
			num = getFarthestNodeAsyncBookmark;
			gotFarthestNodeAsync = false;
		}
		for (int i = num; i < nodesTempArray.Length; i++)
		{
			if (doAsync && i - getFarthestNodeAsyncBookmark > maxAsyncIterations)
			{
				gotFarthestNodeAsync = false;
				getFarthestNodeAsyncBookmark = i;
				return null;
			}
			if ((!capDistance || !(Vector3.Distance(base.transform.position, nodesTempArray[i].transform.position) > 60f)) && !PathIsIntersectedByLineOfSight(nodesTempArray[i].transform.position, calculatePathDistance: false, avoidLineOfSight))
			{
				mostOptimalDistance = Vector3.Distance(pos, nodesTempArray[i].transform.position);
				result = nodesTempArray[i].transform;
				if (offset == 0 || i >= nodesTempArray.Length - 1)
				{
					break;
				}
				offset--;
			}
		}
		getFarthestNodeAsyncBookmark = 0;
		gotFarthestNodeAsync = true;
		return result;
	}

	public Transform ChooseClosestNodeToPosition(Vector3 pos, bool avoidLineOfSight = false, int offset = 0)
	{
		nodesTempArray = allAINodes.OrderBy((GameObject x) => Vector3.Distance(pos, x.transform.position)).ToArray();
		Transform result = nodesTempArray[0].transform;
		for (int i = 0; i < nodesTempArray.Length; i++)
		{
			if (!PathIsIntersectedByLineOfSight(nodesTempArray[i].transform.position, calculatePathDistance: false, avoidLineOfSight))
			{
				mostOptimalDistance = Vector3.Distance(pos, nodesTempArray[i].transform.position);
				result = nodesTempArray[i].transform;
				if (offset == 0 || i >= nodesTempArray.Length - 1)
				{
					break;
				}
				offset--;
			}
		}
		return result;
	}

	public bool PathIsIntersectedByLineOfSight(Vector3 targetPos, bool calculatePathDistance = false, bool avoidLineOfSight = true, bool checkLOSToTargetPlayer = false)
	{
		pathDistance = 0f;
		if (agent.isOnNavMesh && !agent.CalculatePath(targetPos, path1))
		{
			if (DebugEnemy)
			{
				Debug.Log("Path could not be calculated");
			}
			return true;
		}
		if (DebugEnemy)
		{
			for (int i = 1; i < path1.corners.Length; i++)
			{
				Debug.DrawLine(path1.corners[i - 1], path1.corners[i], Color.red);
			}
		}
		if (path1 == null || path1.corners.Length == 0)
		{
			return true;
		}
		if (Vector3.Distance(path1.corners[path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(targetPos, RoundManager.Instance.navHit, 2.7f)) > 1.5f)
		{
			if (DebugEnemy)
			{
				Debug.Log($"Path is not complete; final waypoint of path was too far from target position: {targetPos}");
			}
			return true;
		}
		bool flag = false;
		if (calculatePathDistance)
		{
			for (int j = 1; j < path1.corners.Length; j++)
			{
				pathDistance += Vector3.Distance(path1.corners[j - 1], path1.corners[j]);
				if ((!avoidLineOfSight && !checkLOSToTargetPlayer) || j > 15)
				{
					continue;
				}
				if (!flag && j > 8 && Vector3.Distance(path1.corners[j - 1], path1.corners[j]) < 2f)
				{
					if (DebugEnemy)
					{
						Debug.Log($"Distance between corners {j} and {j - 1} under 3 meters; skipping LOS check");
						Debug.DrawRay(path1.corners[j - 1] + Vector3.up * 0.2f, path1.corners[j] + Vector3.up * 0.2f, Color.magenta, 0.2f);
					}
					flag = true;
					continue;
				}
				flag = false;
				if (targetPlayer != null && checkLOSToTargetPlayer && !Physics.Linecast(path1.corners[j - 1], targetPlayer.transform.position + Vector3.up * 0.3f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
				{
					return true;
				}
				if (avoidLineOfSight && Physics.Linecast(path1.corners[j - 1], path1.corners[j], 262144))
				{
					if (DebugEnemy)
					{
						Debug.Log($"{enemyType.enemyName}: The path is blocked by line of sight at corner {j}");
					}
					return true;
				}
			}
		}
		else if (avoidLineOfSight)
		{
			for (int k = 1; k < path1.corners.Length; k++)
			{
				if (DebugEnemy)
				{
					Debug.DrawLine(path1.corners[k - 1], path1.corners[k], Color.green);
				}
				if (!flag && k > 8 && Vector3.Distance(path1.corners[k - 1], path1.corners[k]) < 2f)
				{
					if (DebugEnemy)
					{
						Debug.Log($"Distance between corners {k} and {k - 1} under 3 meters; skipping LOS check");
						Debug.DrawRay(path1.corners[k - 1] + Vector3.up * 0.2f, path1.corners[k] + Vector3.up * 0.2f, Color.magenta, 0.2f);
					}
					flag = true;
					continue;
				}
				if (targetPlayer != null && checkLOSToTargetPlayer && !Physics.Linecast(Vector3.Lerp(path1.corners[k - 1], path1.corners[k], 0.5f) + Vector3.up * 0.25f, targetPlayer.transform.position + Vector3.up * 0.25f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
				{
					return true;
				}
				if (Physics.Linecast(path1.corners[k - 1], path1.corners[k], 262144))
				{
					if (DebugEnemy)
					{
						Debug.Log($"{enemyType.enemyName}: The path is blocked by line of sight at corner {k}");
					}
					return true;
				}
				if (k > 15)
				{
					if (DebugEnemy)
					{
						Debug.Log(enemyType.enemyName + ": Reached corner 15, stopping checks now");
					}
					return false;
				}
			}
		}
		return false;
	}

	public bool GetPathDistance(Vector3 targetPos, Vector3 sourcePos)
	{
		pathDistance = 0f;
		if (!NavMesh.CalculatePath(sourcePos, targetPos, agent.areaMask, path1))
		{
			if (DebugEnemy)
			{
				Debug.Log("GetPathDistance: Path could not be calculated");
			}
			return false;
		}
		if (path1 == null || path1.corners.Length == 0)
		{
			return false;
		}
		if (Vector3.Distance(path1.corners[path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(targetPos, RoundManager.Instance.navHit, 2.7f)) > 1.5f)
		{
			if (DebugEnemy)
			{
				Debug.Log("GetPathDistance: Path is not complete; final waypoint of path was too far from target position");
			}
			return false;
		}
		for (int i = 1; i < path1.corners.Length; i++)
		{
			pathDistance += Vector3.Distance(path1.corners[i - 1], path1.corners[i]);
		}
		return true;
	}

	public virtual void Update()
	{
		if (enemyType.isDaytimeEnemy && !daytimeEnemyLeaving)
		{
			CheckTimeOfDayToLeave();
		}
		if (stunnedIndefinitely <= 0)
		{
			if (stunNormalizedTimer >= 0f)
			{
				stunNormalizedTimer -= Time.deltaTime / enemyType.stunTimeMultiplier;
			}
			else
			{
				stunnedByPlayer = null;
				if (postStunInvincibilityTimer >= 0f)
				{
					postStunInvincibilityTimer -= Time.deltaTime * 5f;
				}
			}
		}
		if (!ventAnimationFinished && timeSinceSpawn < exitVentAnimationTime + 0.005f * (float)RoundManager.Instance.numberOfEnemiesInScene)
		{
			timeSinceSpawn += Time.deltaTime;
			if (!base.IsOwner)
			{
				_ = serverPosition;
				if (serverPosition != Vector3.zero)
				{
					base.transform.position = serverPosition;
					base.transform.eulerAngles = new Vector3(base.transform.eulerAngles.x, targetYRotation, base.transform.eulerAngles.z);
				}
			}
			else if (updateDestinationInterval >= 0f)
			{
				updateDestinationInterval -= Time.deltaTime;
			}
			else
			{
				SyncPositionToClients();
				updateDestinationInterval = 0.1f;
			}
			return;
		}
		if (!inSpecialAnimation && !ventAnimationFinished)
		{
			ventAnimationFinished = true;
			if (creatureAnimator != null)
			{
				creatureAnimator.SetBool("inSpawningAnimation", value: false);
			}
		}
		if (!base.IsOwner)
		{
			if (currentSearch.inProgress)
			{
				StopSearch(currentSearch);
			}
			SetClientCalculatingAI(enable: false);
			if (!inSpecialAnimation)
			{
				if (RoundManager.Instance.currentDungeonType == 4 && Vector3.Distance(base.transform.position, RoundManager.Instance.currentMineshaftElevator.elevatorInsidePoint.position) < 1f)
				{
					serverPosition += RoundManager.Instance.currentMineshaftElevator.elevatorInsidePoint.position - RoundManager.Instance.currentMineshaftElevator.previousElevatorPosition;
				}
				base.transform.position = Vector3.SmoothDamp(base.transform.position, serverPosition, ref tempVelocity, syncMovementSpeed);
				base.transform.eulerAngles = new Vector3(base.transform.eulerAngles.x, Mathf.LerpAngle(base.transform.eulerAngles.y, targetYRotation, 15f * Time.deltaTime), base.transform.eulerAngles.z);
			}
			timeSinceSpawn += Time.deltaTime;
			return;
		}
		if (isEnemyDead)
		{
			SetClientCalculatingAI(enable: false);
			return;
		}
		if (!inSpecialAnimation)
		{
			SetClientCalculatingAI(enable: true);
		}
		if (movingTowardsTargetPlayer && targetPlayer != null)
		{
			if (setDestinationToPlayerInterval <= 0f)
			{
				setDestinationToPlayerInterval = 0.25f;
				destination = RoundManager.Instance.GetNavMeshPosition(targetPlayer.transform.position, RoundManager.Instance.navHit, 2.7f);
			}
			else
			{
				destination = new Vector3(targetPlayer.transform.position.x, destination.y, targetPlayer.transform.position.z);
				setDestinationToPlayerInterval -= Time.deltaTime;
			}
			if (addPlayerVelocityToDestination > 0f)
			{
				if (targetPlayer == GameNetworkManager.Instance.localPlayerController)
				{
					destination += Vector3.Normalize(targetPlayer.thisController.velocity * 100f) * addPlayerVelocityToDestination;
				}
				else if (targetPlayer.timeSincePlayerMoving < 0.25f)
				{
					destination += Vector3.Normalize((targetPlayer.serverPlayerPosition - targetPlayer.oldPlayerPosition) * 100f) * addPlayerVelocityToDestination;
				}
			}
		}
		if (inSpecialAnimation)
		{
			return;
		}
		if (updateDestinationInterval >= 0f)
		{
			updateDestinationInterval -= Time.deltaTime;
		}
		else
		{
			DoAIInterval();
			updateDestinationInterval = AIIntervalTime + UnityEngine.Random.Range(-0.015f, 0.015f);
		}
		if (Mathf.Abs(previousYRotation - base.transform.eulerAngles.y) > 6f)
		{
			previousYRotation = base.transform.eulerAngles.y;
			targetYRotation = previousYRotation;
			if (base.IsServer)
			{
				UpdateEnemyRotationClientRpc((short)previousYRotation);
			}
			else
			{
				UpdateEnemyRotationServerRpc((short)previousYRotation);
			}
		}
	}

	public void KillEnemyOnOwnerClient(bool overrideDestroy = false)
	{
		if (!base.IsOwner)
		{
			return;
		}
		bool flag = enemyType.destroyOnDeath;
		if (overrideDestroy)
		{
			flag = true;
		}
		if ((!enemyType.canDie && !flag) || isEnemyDead)
		{
			return;
		}
		Debug.Log($"Kill enemy called! destroy: {flag}");
		if (flag)
		{
			if (base.IsServer)
			{
				Debug.Log("Kill enemy called on server, destroy true");
				KillEnemy(destroy: true);
			}
			else
			{
				KillEnemyServerRpc(destroy: true);
			}
		}
		else
		{
			KillEnemy();
			if (base.NetworkObject.IsSpawned)
			{
				KillEnemyServerRpc(destroy: false);
			}
		}
	}

	[ServerRpc(RequireOwnership = false)]
	public void KillEnemyServerRpc(bool destroy)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager == null || !networkManager.IsListening)
		{
			return;
		}
		if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
		{
			ServerRpcParams serverRpcParams = default(ServerRpcParams);
			FastBufferWriter bufferWriter = __beginSendServerRpc(1810146992u, serverRpcParams, RpcDelivery.Reliable);
			bufferWriter.WriteValueSafe(in destroy, default(FastBufferWriter.ForPrimitives));
			__endSendServerRpc(ref bufferWriter, 1810146992u, serverRpcParams, RpcDelivery.Reliable);
		}
		if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
		{
			Debug.Log($"Kill enemy server rpc called with destroy {destroy}");
			if (destroy)
			{
				KillEnemy(destroy);
			}
			else
			{
				KillEnemyClientRpc(destroy);
			}
		}
	}

	[ClientRpc]
	public void KillEnemyClientRpc(bool destroy)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager == null || !networkManager.IsListening)
		{
			return;
		}
		if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
		{
			ClientRpcParams clientRpcParams = default(ClientRpcParams);
			FastBufferWriter bufferWriter = __beginSendClientRpc(1614111717u, clientRpcParams, RpcDelivery.Reliable);
			bufferWriter.WriteValueSafe(in destroy, default(FastBufferWriter.ForPrimitives));
			__endSendClientRpc(ref bufferWriter, 1614111717u, clientRpcParams, RpcDelivery.Reliable);
		}
		if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
		{
			Debug.Log($"Kill enemy client rpc called; {destroy}");
			if (!isEnemyDead)
			{
				KillEnemy(destroy);
			}
		}
	}

	public virtual void KillEnemy(bool destroy = false)
	{
		if (destroy && enemyType.canBeDestroyed)
		{
			Debug.Log("Destroy enemy called");
			if (base.IsServer)
			{
				Debug.Log("Despawn network object in kill enemy called!");
				if (thisNetworkObject.IsSpawned)
				{
					thisNetworkObject.Despawn();
				}
			}
		}
		else
		{
			if (!enemyType.canDie)
			{
				return;
			}
			ScanNodeProperties componentInChildren = base.gameObject.GetComponentInChildren<ScanNodeProperties>();
			if (componentInChildren != null && (bool)componentInChildren.gameObject.GetComponent<Collider>())
			{
				componentInChildren.gameObject.GetComponent<Collider>().enabled = false;
			}
			isEnemyDead = true;
			if (creatureVoice != null)
			{
				creatureVoice.PlayOneShot(dieSFX);
			}
			try
			{
				if (creatureAnimator != null)
				{
					creatureAnimator.SetBool("Stunned", value: false);
					creatureAnimator.SetBool("stunned", value: false);
					creatureAnimator.SetBool("stun", value: false);
					creatureAnimator.SetTrigger("KillEnemy");
					creatureAnimator.SetBool("Dead", value: true);
				}
			}
			catch (Exception arg)
			{
				Debug.LogError($"enemy did not have bool in animator in KillEnemy, error returned; {arg}");
			}
			CancelSpecialAnimationWithPlayer();
			SubtractFromPowerLevel();
			agent.enabled = false;
		}
	}

	public virtual void CancelSpecialAnimationWithPlayer()
	{
		if ((bool)inSpecialAnimationWithPlayer)
		{
			inSpecialAnimationWithPlayer.inSpecialInteractAnimation = false;
			inSpecialAnimationWithPlayer.snapToServerPosition = false;
			inSpecialAnimationWithPlayer.inAnimationWithEnemy = null;
			inSpecialAnimationWithPlayer = null;
		}
		inSpecialAnimation = false;
	}

	public override void OnDestroy()
	{
		base.OnDestroy();
		if (RoundManager.Instance.SpawnedEnemies.Contains(this))
		{
			RoundManager.Instance.SpawnedEnemies.Remove(this);
		}
		SubtractFromPowerLevel();
		CancelSpecialAnimationWithPlayer();
		if (searchCoroutine != null)
		{
			StopCoroutine(searchCoroutine);
		}
		if (chooseTargetNodeCoroutine != null)
		{
			StopCoroutine(chooseTargetNodeCoroutine);
		}
	}

	private void SubtractFromPowerLevel()
	{
		if (!removedPowerLevel)
		{
			removedPowerLevel = true;
			if (enemyType.isDaytimeEnemy)
			{
				RoundManager.Instance.currentDaytimeEnemyPower = Mathf.Max(RoundManager.Instance.currentDaytimeEnemyPower - enemyType.PowerLevel, 0f);
				return;
			}
			if (enemyType.isOutsideEnemy)
			{
				RoundManager.Instance.currentOutsideEnemyPower = Mathf.Max(RoundManager.Instance.currentOutsideEnemyPower - enemyType.PowerLevel, 0f);
				return;
			}
			RoundManager.Instance.cannotSpawnMoreInsideEnemies = false;
			RoundManager.Instance.currentEnemyPower = Mathf.Max(RoundManager.Instance.currentEnemyPower - enemyType.PowerLevel, 0f);
		}
	}

	[ServerRpc]
	private void UpdateEnemyRotationServerRpc(short rotationY)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager == null || !networkManager.IsListening)
		{
			return;
		}
		if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
		{
			if (base.OwnerClientId != networkManager.LocalClientId)
			{
				if (networkManager.LogLevel <= LogLevel.Normal)
				{
					Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
				}
				return;
			}
			ServerRpcParams serverRpcParams = default(ServerRpcParams);
			FastBufferWriter bufferWriter = __beginSendServerRpc(3079913705u, serverRpcParams, RpcDelivery.Reliable);
			BytePacker.WriteValueBitPacked(bufferWriter, rotationY);
			__endSendServerRpc(ref bufferWriter, 3079913705u, serverRpcParams, RpcDelivery.Reliable);
		}
		if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
		{
			UpdateEnemyRotationClientRpc(rotationY);
		}
	}

	[ClientRpc]
	private void UpdateEnemyRotationClientRpc(short rotationY)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
			{
				ClientRpcParams clientRpcParams = default(ClientRpcParams);
				FastBufferWriter bufferWriter = __beginSendClientRpc(1258118513u, clientRpcParams, RpcDelivery.Reliable);
				BytePacker.WriteValueBitPacked(bufferWriter, rotationY);
				__endSendClientRpc(ref bufferWriter, 1258118513u, clientRpcParams, RpcDelivery.Reliable);
			}
			if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
			{
				previousYRotation = base.transform.eulerAngles.y;
				targetYRotation = rotationY;
			}
		}
	}

	[ServerRpc]
	private void UpdateEnemyPositionServerRpc(Vector3 newPos)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager == null || !networkManager.IsListening)
		{
			return;
		}
		if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
		{
			if (base.OwnerClientId != networkManager.LocalClientId)
			{
				if (networkManager.LogLevel <= LogLevel.Normal)
				{
					Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
				}
				return;
			}
			ServerRpcParams serverRpcParams = default(ServerRpcParams);
			FastBufferWriter bufferWriter = __beginSendServerRpc(255411420u, serverRpcParams, RpcDelivery.Reliable);
			bufferWriter.WriteValueSafe(in newPos);
			__endSendServerRpc(ref bufferWriter, 255411420u, serverRpcParams, RpcDelivery.Reliable);
		}
		if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
		{
			UpdateEnemyPositionClientRpc(newPos);
		}
	}

	[ClientRpc]
	private void UpdateEnemyPositionClientRpc(Vector3 newPos)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
			{
				ClientRpcParams clientRpcParams = default(ClientRpcParams);
				FastBufferWriter bufferWriter = __beginSendClientRpc(4287979896u, clientRpcParams, RpcDelivery.Reliable);
				bufferWriter.WriteValueSafe(in newPos);
				__endSendClientRpc(ref bufferWriter, 4287979896u, clientRpcParams, RpcDelivery.Reliable);
			}
			if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost) && !base.IsOwner)
			{
				serverPosition = newPos;
				OnSyncPositionFromServer(newPos);
			}
		}
	}

	public virtual void OnSyncPositionFromServer(Vector3 newPos)
	{
	}

	public virtual void OnDrawGizmos()
	{
		if (base.IsOwner && debugEnemyAI)
		{
			Gizmos.DrawSphere(destination, 0.5f);
			Gizmos.DrawLine(base.transform.position, destination);
		}
	}

	public void ChangeOwnershipOfEnemy(ulong newOwnerClientId)
	{
		if (StartOfRound.Instance.ClientPlayerList.TryGetValue(newOwnerClientId, out var value))
		{
			Debug.Log($"Switching ownership of {enemyType.name} #{thisEnemyIndex} to player #{value} ({StartOfRound.Instance.allPlayerScripts[value].playerUsername})");
			if (currentOwnershipOnThisClient == value)
			{
				Debug.Log($"unable to set owner of {enemyType.name} #{thisEnemyIndex} to player #{value}; reason B; {base.NetworkObject.OwnerClientId}");
				return;
			}
			ulong ownerClientId = base.gameObject.GetComponent<NetworkObject>().OwnerClientId;
			if (ownerClientId == newOwnerClientId)
			{
				Debug.Log($"unable to set owner of {enemyType.name} #{thisEnemyIndex} to player #{value} with id {newOwnerClientId}; current ownerclientId: {ownerClientId}");
			}
			else
			{
				Debug.Log($"{enemyType.enemyName}: setting ownership to {value} from currentownershiponclient {currentOwnershipOnThisClient}");
				currentOwnershipOnThisClient = value;
				if (!base.IsServer)
				{
					ChangeEnemyOwnerServerRpc(newOwnerClientId);
					return;
				}
				thisNetworkObject.ChangeOwnership(newOwnerClientId);
				ChangeEnemyOwnerServerRpc(newOwnerClientId);
			}
		}
		else
		{
			Debug.LogError($"Attempted to switch ownership of enemy {base.gameObject.name} to a player which does not have a link between client id and player object. Attempted clientId: {newOwnerClientId}");
		}
	}

	[ServerRpc(RequireOwnership = false)]
	public void ChangeEnemyOwnerServerRpc(ulong clientId)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager == null || !networkManager.IsListening)
		{
			return;
		}
		if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
		{
			ServerRpcParams serverRpcParams = default(ServerRpcParams);
			FastBufferWriter bufferWriter = __beginSendServerRpc(3587030867u, serverRpcParams, RpcDelivery.Reliable);
			BytePacker.WriteValueBitPacked(bufferWriter, clientId);
			__endSendServerRpc(ref bufferWriter, 3587030867u, serverRpcParams, RpcDelivery.Reliable);
		}
		if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
		{
			if (base.gameObject.GetComponent<NetworkObject>().OwnerClientId != clientId)
			{
				thisNetworkObject.ChangeOwnership(clientId);
			}
			if (StartOfRound.Instance.ClientPlayerList.TryGetValue(clientId, out var value))
			{
				currentOwnershipOnThisClient = value;
				ChangeEnemyOwnerClientRpc(value);
			}
		}
	}

	[ClientRpc]
	public void ChangeEnemyOwnerClientRpc(int playerVal)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
			{
				ClientRpcParams clientRpcParams = default(ClientRpcParams);
				FastBufferWriter bufferWriter = __beginSendClientRpc(245785831u, clientRpcParams, RpcDelivery.Reliable);
				BytePacker.WriteValueBitPacked(bufferWriter, playerVal);
				__endSendClientRpc(ref bufferWriter, 245785831u, clientRpcParams, RpcDelivery.Reliable);
			}
			if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost) && !base.IsServer)
			{
				currentOwnershipOnThisClient = playerVal;
			}
		}
	}

	public void SetClientCalculatingAI(bool enable)
	{
		isClientCalculatingAI = enable;
		agent.enabled = enable;
	}

	public virtual void EnableEnemyMesh(bool enable, bool overrideDoNotSet = false)
	{
		int layer = ((!enable) ? 23 : 19);
		for (int i = 0; i < skinnedMeshRenderers.Length; i++)
		{
			if (!skinnedMeshRenderers[i].CompareTag("DoNotSet") || overrideDoNotSet)
			{
				skinnedMeshRenderers[i].gameObject.layer = layer;
			}
		}
		for (int j = 0; j < meshRenderers.Length; j++)
		{
			if (!meshRenderers[j].CompareTag("DoNotSet") || overrideDoNotSet)
			{
				meshRenderers[j].gameObject.layer = layer;
			}
		}
	}

	public virtual void SetEnemyOutside(bool outside = false)
	{
		isOutside = outside;
		if (outside)
		{
			allAINodes = GameObject.FindGameObjectsWithTag("OutsideAINode");
		}
		else
		{
			allAINodes = GameObject.FindGameObjectsWithTag("AINode");
		}
	}

	public virtual void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
	{
	}

	public virtual void HitFromExplosion(float distance)
	{
		if (debugEnemyAI)
		{
			Debug.Log($"{enemyType.enemyName} #{thisEnemyIndex} hit by explosion");
		}
	}

	public void HitEnemyOnLocalClient(int force = 1, Vector3 hitDirection = default(Vector3), PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
	{
		Debug.Log($"Local client hit enemy {agent.transform.name} #{thisEnemyIndex} with force of {force}.");
		int playerWhoHit2 = -1;
		if (playerWhoHit != null)
		{
			playerWhoHit2 = (int)playerWhoHit.playerClientId;
			HitEnemy(force, playerWhoHit, playHitSFX, hitID);
		}
		HitEnemyServerRpc(force, playerWhoHit2, playHitSFX, hitID);
	}

	[ServerRpc(RequireOwnership = false)]
	public void HitEnemyServerRpc(int force, int playerWhoHit, bool playHitSFX, int hitID = -1)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
			{
				ServerRpcParams serverRpcParams = default(ServerRpcParams);
				FastBufferWriter bufferWriter = __beginSendServerRpc(3538577804u, serverRpcParams, RpcDelivery.Reliable);
				BytePacker.WriteValueBitPacked(bufferWriter, force);
				BytePacker.WriteValueBitPacked(bufferWriter, playerWhoHit);
				bufferWriter.WriteValueSafe(in playHitSFX, default(FastBufferWriter.ForPrimitives));
				BytePacker.WriteValueBitPacked(bufferWriter, hitID);
				__endSendServerRpc(ref bufferWriter, 3538577804u, serverRpcParams, RpcDelivery.Reliable);
			}
			if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
			{
				HitEnemyClientRpc(force, playerWhoHit, playHitSFX, hitID);
			}
		}
	}

	[ClientRpc]
	public void HitEnemyClientRpc(int force, int playerWhoHit, bool playHitSFX, int hitID = -1)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager == null || !networkManager.IsListening)
		{
			return;
		}
		if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
		{
			ClientRpcParams clientRpcParams = default(ClientRpcParams);
			FastBufferWriter bufferWriter = __beginSendClientRpc(601871377u, clientRpcParams, RpcDelivery.Reliable);
			BytePacker.WriteValueBitPacked(bufferWriter, force);
			BytePacker.WriteValueBitPacked(bufferWriter, playerWhoHit);
			bufferWriter.WriteValueSafe(in playHitSFX, default(FastBufferWriter.ForPrimitives));
			BytePacker.WriteValueBitPacked(bufferWriter, hitID);
			__endSendClientRpc(ref bufferWriter, 601871377u, clientRpcParams, RpcDelivery.Reliable);
		}
		if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost) && playerWhoHit != (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
		{
			if (playerWhoHit == -1)
			{
				HitEnemy(force, null, playHitSFX, hitID);
			}
			else
			{
				HitEnemy(force, StartOfRound.Instance.allPlayerScripts[playerWhoHit], playHitSFX, hitID);
			}
		}
	}

	public virtual void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
	{
		if (playHitSFX && enemyType.hitBodySFX != null)
		{
			creatureSFX.PlayOneShot(enemyType.hitBodySFX);
			WalkieTalkie.TransmitOneShotAudio(creatureSFX, enemyType.hitBodySFX);
		}
		if (creatureVoice != null)
		{
			creatureVoice.PlayOneShot(enemyType.hitEnemyVoiceSFX);
		}
		if (debugEnemyAI)
		{
			Debug.Log($"Enemy #{thisEnemyIndex} was hit with force of {force}");
		}
		if (playerWhoHit != null)
		{
			Debug.Log($"Client #{playerWhoHit.playerClientId} hit enemy {agent.transform.name} with force of {force}.");
		}
	}

	public virtual void ReceiveLoudNoiseBlast(Vector3 position, float angle)
	{
		float num = Vector3.Distance(position, base.transform.position);
		if (num < 60f)
		{
			float num2 = 0f;
			if (angle < 35f)
			{
				num2 = ((!(num < 25f)) ? 0.7f : 1f);
				DetectNoise(position, num2, 0, 41888);
			}
			else if (angle < 16f)
			{
				num2 = 1f;
				DetectNoise(position, num2, 0, 41888);
			}
		}
	}

	private void CheckTimeOfDayToLeave()
	{
		if (!(TimeOfDay.Instance == null) && TimeOfDay.Instance.timeHasStarted && TimeOfDay.Instance.normalizedTimeOfDay > enemyType.normalizedTimeInDayToLeave && isOutside)
		{
			daytimeEnemyLeaving = true;
			DaytimeEnemyLeave();
		}
	}

	public virtual void DaytimeEnemyLeave()
	{
		if (debugEnemyAI)
		{
			Debug.Log(base.gameObject.name + ": Daytime enemy leave function called");
		}
	}

	public void LogEnemyError(string error)
	{
		Debug.LogError($"{enemyType.name} #{thisEnemyIndex}: {error}");
	}

	public virtual void AnimationEventA()
	{
	}

	public virtual void AnimationEventB()
	{
	}

	public virtual void ShipTeleportEnemy()
	{
	}

	protected override void __initializeVariables()
	{
		base.__initializeVariables();
	}

	[RuntimeInitializeOnLoadMethod]
	internal static void InitializeRPCS_EnemyAI()
	{
		NetworkManager.__rpc_func_table.Add(2081148948u, __rpc_handler_2081148948);
		NetworkManager.__rpc_func_table.Add(2962895088u, __rpc_handler_2962895088);
		NetworkManager.__rpc_func_table.Add(1810146992u, __rpc_handler_1810146992);
		NetworkManager.__rpc_func_table.Add(1614111717u, __rpc_handler_1614111717);
		NetworkManager.__rpc_func_table.Add(3079913705u, __rpc_handler_3079913705);
		NetworkManager.__rpc_func_table.Add(1258118513u, __rpc_handler_1258118513);
		NetworkManager.__rpc_func_table.Add(255411420u, __rpc_handler_255411420);
		NetworkManager.__rpc_func_table.Add(4287979896u, __rpc_handler_4287979896);
		NetworkManager.__rpc_func_table.Add(3587030867u, __rpc_handler_3587030867);
		NetworkManager.__rpc_func_table.Add(245785831u, __rpc_handler_245785831);
		NetworkManager.__rpc_func_table.Add(3538577804u, __rpc_handler_3538577804);
		NetworkManager.__rpc_func_table.Add(601871377u, __rpc_handler_601871377);
	}

	private static void __rpc_handler_2081148948(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
	{
		NetworkManager networkManager = target.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			ByteUnpacker.ReadValueBitPacked(reader, out int value);
			target.__rpc_exec_stage = __RpcExecStage.Server;
			((EnemyAI)target).SwitchToBehaviourServerRpc(value);
			target.__rpc_exec_stage = __RpcExecStage.None;
		}
	}

	private static void __rpc_handler_2962895088(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
	{
		NetworkManager networkManager = target.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			ByteUnpacker.ReadValueBitPacked(reader, out int value);
			target.__rpc_exec_stage = __RpcExecStage.Client;
			((EnemyAI)target).SwitchToBehaviourClientRpc(value);
			target.__rpc_exec_stage = __RpcExecStage.None;
		}
	}

	private static void __rpc_handler_1810146992(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
	{
		NetworkManager networkManager = target.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			reader.ReadValueSafe(out bool value, default(FastBufferWriter.ForPrimitives));
			target.__rpc_exec_stage = __RpcExecStage.Server;
			((EnemyAI)target).KillEnemyServerRpc(value);
			target.__rpc_exec_stage = __RpcExecStage.None;
		}
	}

	private static void __rpc_handler_1614111717(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
	{
		NetworkManager networkManager = target.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			reader.ReadValueSafe(out bool value, default(FastBufferWriter.ForPrimitives));
			target.__rpc_exec_stage = __RpcExecStage.Client;
			((EnemyAI)target).KillEnemyClientRpc(value);
			target.__rpc_exec_stage = __RpcExecStage.None;
		}
	}

	private static void __rpc_handler_3079913705(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
	{
		NetworkManager networkManager = target.NetworkManager;
		if ((object)networkManager == null || !networkManager.IsListening)
		{
			return;
		}
		if (rpcParams.Server.Receive.SenderClientId != target.OwnerClientId)
		{
			if (networkManager.LogLevel <= LogLevel.Normal)
			{
				Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
			}
		}
		else
		{
			ByteUnpacker.ReadValueBitPacked(reader, out short value);
			target.__rpc_exec_stage = __RpcExecStage.Server;
			((EnemyAI)target).UpdateEnemyRotationServerRpc(value);
			target.__rpc_exec_stage = __RpcExecStage.None;
		}
	}

	private static void __rpc_handler_1258118513(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
	{
		NetworkManager networkManager = target.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			ByteUnpacker.ReadValueBitPacked(reader, out short value);
			target.__rpc_exec_stage = __RpcExecStage.Client;
			((EnemyAI)target).UpdateEnemyRotationClientRpc(value);
			target.__rpc_exec_stage = __RpcExecStage.None;
		}
	}

	private static void __rpc_handler_255411420(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
	{
		NetworkManager networkManager = target.NetworkManager;
		if ((object)networkManager == null || !networkManager.IsListening)
		{
			return;
		}
		if (rpcParams.Server.Receive.SenderClientId != target.OwnerClientId)
		{
			if (networkManager.LogLevel <= LogLevel.Normal)
			{
				Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
			}
		}
		else
		{
			reader.ReadValueSafe(out Vector3 value);
			target.__rpc_exec_stage = __RpcExecStage.Server;
			((EnemyAI)target).UpdateEnemyPositionServerRpc(value);
			target.__rpc_exec_stage = __RpcExecStage.None;
		}
	}

	private static void __rpc_handler_4287979896(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
	{
		NetworkManager networkManager = target.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			reader.ReadValueSafe(out Vector3 value);
			target.__rpc_exec_stage = __RpcExecStage.Client;
			((EnemyAI)target).UpdateEnemyPositionClientRpc(value);
			target.__rpc_exec_stage = __RpcExecStage.None;
		}
	}

	private static void __rpc_handler_3587030867(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
	{
		NetworkManager networkManager = target.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			ByteUnpacker.ReadValueBitPacked(reader, out ulong value);
			target.__rpc_exec_stage = __RpcExecStage.Server;
			((EnemyAI)target).ChangeEnemyOwnerServerRpc(value);
			target.__rpc_exec_stage = __RpcExecStage.None;
		}
	}

	private static void __rpc_handler_245785831(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
	{
		NetworkManager networkManager = target.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			ByteUnpacker.ReadValueBitPacked(reader, out int value);
			target.__rpc_exec_stage = __RpcExecStage.Client;
			((EnemyAI)target).ChangeEnemyOwnerClientRpc(value);
			target.__rpc_exec_stage = __RpcExecStage.None;
		}
	}

	private static void __rpc_handler_3538577804(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
	{
		NetworkManager networkManager = target.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			ByteUnpacker.ReadValueBitPacked(reader, out int value);
			ByteUnpacker.ReadValueBitPacked(reader, out int value2);
			reader.ReadValueSafe(out bool value3, default(FastBufferWriter.ForPrimitives));
			ByteUnpacker.ReadValueBitPacked(reader, out int value4);
			target.__rpc_exec_stage = __RpcExecStage.Server;
			((EnemyAI)target).HitEnemyServerRpc(value, value2, value3, value4);
			target.__rpc_exec_stage = __RpcExecStage.None;
		}
	}

	private static void __rpc_handler_601871377(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
	{
		NetworkManager networkManager = target.NetworkManager;
		if ((object)networkManager != null && networkManager.IsListening)
		{
			ByteUnpacker.ReadValueBitPacked(reader, out int value);
			ByteUnpacker.ReadValueBitPacked(reader, out int value2);
			reader.ReadValueSafe(out bool value3, default(FastBufferWriter.ForPrimitives));
			ByteUnpacker.ReadValueBitPacked(reader, out int value4);
			target.__rpc_exec_stage = __RpcExecStage.Client;
			((EnemyAI)target).HitEnemyClientRpc(value, value2, value3, value4);
			target.__rpc_exec_stage = __RpcExecStage.None;
		}
	}

	protected internal override string __getTypeName()
	{
		return "EnemyAI";
	}
}
