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

	public AudioClip[] hitSFX;

	public AudioClip[] swingSFX;

	private int stickMask = 1084754248;

	private float timeAtLastDamageDealt;

	public override void ItemActivate(bool used, bool buttonDown = true)
	{
		RoundManager.PlayRandomClip(stickAudio, swingSFX);
		if (playerHeldBy != null)
		{
			previousPlayerHeldBy = playerHeldBy;
			if (playerHeldBy.IsOwner)
			{
				//playerHeldBy.playerBodyAnimator.SetTrigger("UseHeldItem1");
			}
		}
		if (base.IsOwner)
		{
			HitStick();
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

	public void HitStick(bool cancel = false)
	{
		if (previousPlayerHeldBy == null)
		{
			Debug.LogError("Previousplayerheldby is null on this client when HitStickC is called.");
			return;
		}
		previousPlayerHeldBy.activatingItem = false;
		bool flag = false;
		bool flag2 = false;
		int num = -1;
		if (!cancel && Time.realtimeSinceStartup - timeAtLastDamageDealt > 0.43f)
		{
			previousPlayerHeldBy.twoHanded = false;
			objectsHitByStick = Physics.SphereCastAll(previousPlayerHeldBy.gameplayCamera.transform.position + previousPlayerHeldBy.gameplayCamera.transform.right * 0.1f, 0.3f, previousPlayerHeldBy.gameplayCamera.transform.forward, 0.75f, stickMask, QueryTriggerInteraction.Collide);
			objectsHitByStickList = objectsHitByStick.OrderBy((RaycastHit x) => x.distance).ToList();
			List<EnemyAI> list = new List<EnemyAI>();
			for (int i = 0; i < objectsHitByStickList.Count; i++)
			{
				if (objectsHitByStickList[i].transform.gameObject.layer == 8 || objectsHitByStickList[i].transform.gameObject.layer == 11)
				{
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
					if (!objectsHitByStickList[i].transform.TryGetComponent<IHittable>(out var component) || objectsHitByStickList[i].transform == previousPlayerHeldBy.transform || (!(objectsHitByStickList[i].point == Vector3.zero) && Physics.Linecast(previousPlayerHeldBy.gameplayCamera.transform.position, objectsHitByStickList[i].point, out var _, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)))
					{
						continue;
					}
					flag = true;
					Vector3 forward = previousPlayerHeldBy.gameplayCamera.transform.forward;
					try
					{
						PlayerControllerB playerHit = objectsHitByStickList[i].transform.GetComponent<PlayerControllerB>();
						if(playerHit != null)
                        {
							float num3 = Vector3.Distance(playerHit.transform.position, transform.position);
							Vector3 vector = Vector3.Normalize(playerHit.transform.position + Vector3.up * num3 - transform.position) / (num3 * 0.35f) * 2f;
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
		if (flag)
		{
			//RoundManager.PlayRandomClip(stickAudio, hitSFX);
			UnityEngine.Object.FindObjectOfType<RoundManager>().PlayAudibleNoise(base.transform.position, 17f, 0.8f);
			if (!flag2 && num != -1)
			{
				stickAudio.PlayOneShot(StartOfRound.Instance.footstepSurfaces[num].hitSurfaceSFX);
				WalkieTalkie.TransmitOneShotAudio(stickAudio, StartOfRound.Instance.footstepSurfaces[num].hitSurfaceSFX);
			}
			HitStickCServerRpc(num);
		}
	}

	[ServerRpc]
	public void HitStickCServerRpc(int hitSurfaceID)
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
			FastBufferWriter bufferWriter = __beginSendServerRpc(2696735117u, serverRpcParams, RpcDelivery.Reliable);
			BytePacker.WriteValueBitPacked(bufferWriter, hitSurfaceID);
			__endSendServerRpc(ref bufferWriter, 2696735117u, serverRpcParams, RpcDelivery.Reliable);
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
		if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
		{
			ClientRpcParams clientRpcParams = default(ClientRpcParams);
			FastBufferWriter bufferWriter = __beginSendClientRpc(3250235443u, clientRpcParams, RpcDelivery.Reliable);
			BytePacker.WriteValueBitPacked(bufferWriter, hitSurfaceID);
			__endSendClientRpc(ref bufferWriter, 3250235443u, clientRpcParams, RpcDelivery.Reliable);
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
