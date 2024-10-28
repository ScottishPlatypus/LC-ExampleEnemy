// Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// StickItem
using System;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

public class StickItem : GrabbableObject
{
	public AudioSource stickAudio;

	private List<RaycastHit> objectsHitByStickList = new List<RaycastHit>();

	public PlayerControllerB previousPlayerHeldBy;

	private RaycastHit[] objectsHitByStick;

	public int stickHitForce;

	public AudioClip hitSFX;

	public AudioClip swingSFX;

	private int stickMask = 1084754248;

	private float timeAtLastDamageDealt;

    public override void Start()
    {
        base.Start();
		Debug.Log("On Start");
	}

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
		Debug.Log("Has spawned");
	}

    public override void ItemActivate(bool used, bool buttonDown = true)
	{
		if (playerHeldBy != null)
		{
			previousPlayerHeldBy = playerHeldBy;
			if (playerHeldBy.IsOwner)
			{
				playerHeldBy.playerBodyAnimator.SetTrigger("UseHeldItem1");
			}
		}
		if (base.IsOwner)
		{
			HitStickClientRpc();
		}
	}

	public override void PocketItem()
	{
		base.PocketItem();
	}

	public override void DiscardItem()
	{
		base.DiscardItem();
	}

	public override void EquipItem()
	{
		base.EquipItem();
	}

	public override void GrabItem()
	{
		base.EquipItem();
	}

	[ClientRpc]
	public void HitStickClientRpc(bool cancel = false)
	{
		Debug.Log("use stick");
		stickAudio.PlayOneShot(swingSFX);
		if(IsServer)
			SwingStickServerRpc();

		Debug.Log("PlayerHeldBy : " + playerHeldBy != null);
		Debug.Log(playerHeldBy.GetComponent<BoxCollider>().size.y);

		if (playerHeldBy == null)
        {
			return;
			Debug.LogError("Previousplayerheldby is null on this client when HitStick is called.");
		}

		previousPlayerHeldBy = playerHeldBy;
		previousPlayerHeldBy.activatingItem = false;
		bool flag = false;
		bool flag2 = false;
		int num = -1;
		if (!cancel && Time.realtimeSinceStartup - timeAtLastDamageDealt > 0.2f && previousPlayerHeldBy != null)
		{
			Debug.Log("Cast sphere");
			previousPlayerHeldBy.twoHanded = false;
			objectsHitByStick = Physics.SphereCastAll(previousPlayerHeldBy.gameplayCamera.transform.position + previousPlayerHeldBy.gameplayCamera.transform.right * 0.1f, 0.3f, previousPlayerHeldBy.gameplayCamera.transform.forward, 0.75f, stickMask, QueryTriggerInteraction.Collide);
			objectsHitByStickList = objectsHitByStick.OrderBy((RaycastHit x) => x.distance).ToList();
			List<EnemyAI> list = new List<EnemyAI>();
			for (int i = 0; i < objectsHitByStickList.Count; i++)
			{
				Debug.Log(objectsHitByStickList[i].transform.gameObject.name);
				if (objectsHitByStickList[i].transform.gameObject.layer == 8 || objectsHitByStickList[i].transform.gameObject.layer == 11)
				{
					Debug.Log("Hit surface");
					flag = true;
					string text = objectsHitByStickList[i].collider.gameObject.tag;
					for (int j = 0; j < StartOfRound.Instance.footstepSurfaces.Length; j++)
					{
						if (StartOfRound.Instance.footstepSurfaces[j].surfaceTag == text)
						{
							num = j;
							break;
						}
					}
				}
				else
				{
					flag = true;
					try
					{
						PlayerControllerB playerHit = objectsHitByStickList[i].transform.GetComponent<PlayerControllerB>();
						if(playerHit != null && playerHit != previousPlayerHeldBy)
                        {
							Debug.Log("hit player : " + playerHit.playerUsername);
							Vector3 hitPos = new Vector3(previousPlayerHeldBy.transform.position.x, playerHeldBy.GetComponent<BoxCollider>().transform.position.y - 1f, previousPlayerHeldBy.transform.position.z);
							float num3 = Vector3.Distance(playerHit.transform.position, hitPos);
							Vector3 vector = Vector3.Normalize(playerHit.transform.position + Vector3.up * num3 - hitPos) / (num3 * 0.35f) * 2f;
							playerHit.externalForceAutoFade += vector;
						}

					}
					catch (Exception arg)
					{
						Debug.Log($"Exception caught when hitting object with stick from player #{previousPlayerHeldBy.playerClientId}: {arg}");
					}
				}
			}
		}
		else
        {
			Debug.Log("Time : " + (Time.realtimeSinceStartup - timeAtLastDamageDealt));
			Debug.Log("cancel : " + cancel);
			Debug.Log("previousPlayerHeldBy: " + previousPlayerHeldBy != null);
		}
		if (flag)
		{
			//RoundManager.PlayRandomClip(stickAudio, hitSFX);
			//UnityEngine.Object.FindObjectOfType<RoundManager>().PlayAudibleNoise(base.transform.position, 17f, 0.8f);
			if (!flag2 && num != -1)
			{
				stickAudio.PlayOneShot(StartOfRound.Instance.footstepSurfaces[num].hitSurfaceSFX);
				WalkieTalkie.TransmitOneShotAudio(stickAudio, StartOfRound.Instance.footstepSurfaces[num].hitSurfaceSFX);
			}
			HitStickServerRpc(num);
		}
	}

	[ClientRpc]
	public void HitPlayerClientRpc(int playerIndex)
	{
		PlayerControllerB playerHit = StartOfRound.Instance.allPlayerScripts[playerIndex];
	
	}

	[ServerRpc]
	public void SwingStickServerRpc()
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
		}
		if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
		{
			SwingStickClientRpc();
		}
	}

	[ClientRpc]
	public void SwingStickClientRpc()
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager == null || !networkManager.IsListening)
		{
			return;
		}
		if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost) && !base.IsOwner)
		{
			stickAudio.PlayOneShot(swingSFX);
		}
	}

	[ServerRpc]
	public void HitStickServerRpc(int hitSurfaceID)
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
		}
		if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
		{
			HitStickClientRpc(hitSurfaceID);
		}
	}

	[ClientRpc]
	public void HitStickClientRpc(int hitSurfaceID)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager == null || !networkManager.IsListening)
		{
			return;
		}
		if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost) && !base.IsOwner)
		{
			//RoundManager.PlayRandomClip(stickAudio, hitSFX);
			if (hitSurfaceID != -1)
			{
				HitSurfaceWithStick(hitSurfaceID);
			}
		}
	}

	private void HitSurfaceWithStick(int hitSurfaceID)
	{
		stickAudio.PlayOneShot(StartOfRound.Instance.footstepSurfaces[hitSurfaceID].hitSurfaceSFX);
		WalkieTalkie.TransmitOneShotAudio(stickAudio, StartOfRound.Instance.footstepSurfaces[hitSurfaceID].hitSurfaceSFX);
	}

	protected override void __initializeVariables()
	{
		base.__initializeVariables();
	}

	protected override string __getTypeName()
	{
		return "StickItem";
	}
}
