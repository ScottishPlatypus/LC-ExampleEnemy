// Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// StickItem
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace CustomEnnemies
{
	public class WandItem : GrabbableObject
	{
		public AudioSource wandAudio;

		public PlayerControllerB previousPlayerHeldBy;

		public AudioClip swingSFX;
		public AudioClip failCast;

		public GameObject spellPrefab;
		GameObject spellInstance;

		public Transform projectileSource;

		public GameObject[] chargeLeftFx;
		public ParticleSystem chargeSpellFx;

		public int charges = 3;

		private bool localClientCastingSpellRPC;

		public override void Start()
		{
			base.Start();

			CheckChargesLeft();
		}

		public override void LoadItemSaveData(int saveData)
		{
			base.LoadItemSaveData(saveData);
			charges = saveData;

			CheckChargesLeft();
		}

		public override int GetItemDataToSave()
		{
			return charges;
		}

		public override void ItemActivate(bool used, bool buttonDown = true)
		{
			base.ItemActivate(used, buttonDown);
			if (playerHeldBy != null)
			{
				if (charges <= 0)
				{
					Debug.Log("No wand charges left");
					if (isHeld && playerHeldBy != null && playerHeldBy == GameNetworkManager.Instance.localPlayerController)
					{
						playerHeldBy.playerBodyAnimator.SetTrigger("UseHeldItem1");
					}
					wandAudio.PlayOneShot(failCast);
				}
				else if (base.IsOwner)
				{
					CastSpellAndSync(heldByPlayer: true);
				}
			}
		}

		public void CastSpellAndSync(bool heldByPlayer)
		{
			Debug.Log("Calling cast spell....");
			CastSpell(projectileSource.position, playerHeldBy.visorCamera.transform.rotation);
			Debug.Log("Calling cast spell and sync");
			localClientCastingSpellRPC = true;
			CastSpellServerRpc(projectileSource.position, playerHeldBy.visorCamera.transform.rotation);
		}

		[ServerRpc(RequireOwnership = false)]
		public void CastSpellServerRpc(Vector3 wandPosition, Quaternion wandRotation)
		{
			CastSpellClientRpc(wandPosition, wandRotation);
		}

		[ClientRpc]
		public void CastSpellClientRpc(Vector3 wandPosition, Quaternion wandRotation)
		{
			Debug.Log("Cast spell client rpc received");
			if (localClientCastingSpellRPC)
			{
				localClientCastingSpellRPC = false;
				Debug.Log("localClientCastingSpellRPC was true");
			}
			else
			{
				CastSpell(wandPosition, wandRotation);
			}
		}

		public void CastSpell(Vector3 wandPosition, Quaternion wandRotation)
		{
			if (isHeld && playerHeldBy != null && playerHeldBy == GameNetworkManager.Instance.localPlayerController)
			{
				playerHeldBy.playerBodyAnimator.SetTrigger("UseHeldItem1");
			}
			else return;

			charges -= 1;
			CheckChargesLeft();

			chargeSpellFx.Play();

			Debug.Log("wand held by : " + playerHeldBy.playerUsername);

			spellInstance = Instantiate(spellPrefab, wandPosition, wandRotation, playerHeldBy.transform.parent);
			spellInstance.GetComponent<NetworkObject>().Spawn();
			spellInstance.GetComponent<MagicSpell>().PlayerLaunch();
		}

		public void CheckChargesLeft()
		{
			for (int i = 0; i < chargeLeftFx.Length; i++)
			{
				chargeLeftFx[i].SetActive(i < charges);
			}
		}

		public override void PocketItem()
		{
			base.PocketItem();

			for (int i = 0; i < chargeLeftFx.Length; i++)
			{
				chargeLeftFx[i].SetActive(false);
			}
		}

		public override void DiscardItem()
		{
			base.DiscardItem();
		}

		public override void EquipItem()
		{
			base.EquipItem();

			CheckChargesLeft();
		}

		public override void GrabItem()
		{
			base.EquipItem();
		}

		protected override void __initializeVariables()
		{
			base.__initializeVariables();
		}
	}
}
