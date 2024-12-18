// Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// PlushItem
using System;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

public class PlushItem : GrabbableObject
{
	public AudioSource plushAudio;

	public AudioClip[] playerClips;

	public Animator plushAnimator;

	public float timer;

	public bool waitForTimer;

	public override void Start()
	{
		base.Start();

		Debug.Log("Spawn Plush ");
	}

	public override void Update()
	{
		base.Update();

		if(timer > 0)
			timer -= Time.deltaTime;
	}

	public override void OnNetworkSpawn()
	{
		base.OnNetworkSpawn();
	}

	public override void ItemActivate(bool used, bool buttonDown = true)
	{
		Debug.Log("Use Item");

		if (playerHeldBy != null)
		{
			if (playerHeldBy.IsOwner)
			{
				//playerHeldBy.playerBodyAnimator.SetTrigger("UseHeldItem1");
			}
		}

		if (timer <= 0 || !waitForTimer)
		{
			int random = UnityEngine.Random.Range(0, playerClips.Length);
			UsePlushServerRpc(random);
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

	[ServerRpc]
	public void UsePlushServerRpc(int random)
	{
		NetworkManager networkManager = base.NetworkManager;
		if ((object)networkManager == null || !networkManager.IsListening)
		{
			return;
		}
		if (__rpc_exec_stage == __RpcExecStage.Server)
		{
			UsePlushClientRpc(random);
		}
	}

	[ClientRpc]
	public void UsePlushClientRpc(int random)
	{
		if (playerClips.Length > 0)
		{
			plushAudio.Stop();
			plushAudio.PlayOneShot(playerClips[random]);
			timer = playerClips[random].length;
		}

		Debug.Log("Play Clip N°" + random);

		plushAnimator.Play("Use");
	}

	protected override void __initializeVariables()
	{
		base.__initializeVariables();
	}

	protected override string __getTypeName()
	{
		return "PlushItem";
	}
}