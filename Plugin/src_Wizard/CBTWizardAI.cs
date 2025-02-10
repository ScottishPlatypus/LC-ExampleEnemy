using System.Collections;
using System.Diagnostics;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace CustomEnnemies
{

    //TEST
    // You may be wondering, how does the Example Enemy know it is from class RandyOrtonAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.

    class CBTWizardAI : EnemyAI
    {
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
#pragma warning restore 0649
        float timeSinceHittingPlayer;
        Vector3 positionRandomness;
        Vector3 spawnPos;
        System.Random enemyRandom = null!;
        bool isDeadAnimationDone;
        bool attackingPlayer;
        bool isRare;
        Vector3 stalkPos;
        private Ray playerRay;

        public Transform dropSpot;

        public GameObject stickPrefab;

        public WandItem stick;

        public GameObject targetDebug;
        public GameObject targetDebugInstance;

        public NetworkObjectReference stickObjectRef;

        public GameObject fireballPrefab;
        GameObject fireballInstance;
        GameObject serverFireballInstance;
        public GameObject fireAura;

        public ParticleSystem launchSpellFx;

        public float hitRange;
        public float maxHitRange;

        float searchTimer;
        float timeSinceNewRandPos;

        enum State
        {
            SearchingForPlayer,
            ChasePlayer,
            AttackPlayer,
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start()
        {
            base.Start();

            if (base.IsServer)
            {
                InitializeWizardServerRpc();
            }

            LogIfDebugBuild("CBT Wizard Spawned");
            LogIfDebugBuild("Is Host : " + IsServer);
            timeSinceHittingPlayer = 0;
            timeSinceNewRandPos = 0;
            DoAnimationClientRpc("startWalk");
            positionRandomness = new Vector3(0, 0, 0);
            agent.acceleration = 30f;
            agent.angularSpeed = 1000f;
            spawnPos = transform.position;
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;
            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            currentBehaviourStateIndex = (int)State.SearchingForPlayer;
            // We make the enemy start searching. This will make it start wandering around.
            searchTimer = 10f;
            StartSearch(transform.position);

            CreateProjectileServerRpc();
        }

        [ServerRpc]
        public void InitializeWizardServerRpc()
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
                    if (networkManager.LogLevel == LogLevel.Normal)
                    {
                        LogIfDebugBuild("Only the owner can invoke a ServerRpc that requires ownership!");
                    }
                    return;
                }
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
		    {
                GameObject gameObject = Instantiate(stickPrefab, transform.position + Vector3.up * 0.5f, Quaternion.identity, RoundManager.Instance.spawnedScrapContainer);
                gameObject.GetComponent<NetworkObject>().Spawn();

                int randomValue = Random.Range(80, 120);
                GrabStick(gameObject, randomValue);

                InitializeWizardClientRpc(gameObject.GetComponent<NetworkObject>());
            }
        }

        [ClientRpc]
        public void InitializeWizardClientRpc(NetworkObjectReference stickObject)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
                LogIfDebugBuild("Set wand ref");
                stickObjectRef = stickObject;
            }
        }

        private void GrabStick(GameObject stickObject, int value)
        {
            stick = stickObject.GetComponent<WandItem>();
            if (stick == null)
            {
                LogEnemyError("Stick in GrabStick function did not contain PhysicsProp component.");
                return;
            }

            LogIfDebugBuild("Set wand value : " + value);
            stick.SetScrapValue(value);
            RoundManager.Instance.totalScrapValueInLevel += stick.scrapValue;
            stick.parentObject = dropSpot;
            stick.isHeldByEnemy = true;
            stick.grabbableToEnemies = false;
            stick.grabbable = false;
            stick.GrabItemFromEnemy(this);
            stickObject.SetActive(false);
        }

        private bool GrabStickIfNotHolding()
        {
            if (stick != null)
            {
                return true;
            }
            if (stickObjectRef.TryGet(out var networkObject))
            {
                LogIfDebugBuild("Grab stick if not holding");
                stick = networkObject.gameObject.GetComponent<WandItem>();
                GrabStick(stick.gameObject, 100);
            }
            return stick != null;
        }

        private void DropStick(Vector3 dropPosition)
        {
            if (stick == null)
            {
                LogEnemyError("Could not drop stick since no stick was held!");
                return;
            }

            stick.gameObject.SetActive(true);
            stick.DiscardItemFromEnemy();
            stick.isHeldByEnemy = false;
            stick.grabbableToEnemies = true;
            stick.grabbable = true;
            stick.transform.position = dropSpot.position;
            stick.transform.rotation = Quaternion.identity;
            stick.isInFactory = true;
        }

        [ServerRpc]
        public void DropStickServerRpc(Vector3 dropPosition)
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
                    if (networkManager.LogLevel == LogLevel.Normal)
                    {
                        LogEnemyError("Only the owner can invoke a ServerRpc that requires ownership!");
                    }
                    return;
                }
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
		    {
                
                DropStickClientRpc(dropPosition);
            }
        }

        [ClientRpc]
        public void DropStickClientRpc(Vector3 dropPosition)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager != null && networkManager.IsListening)
            {
                if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost) && stick != null)
			    {
                    DropStick(dropPosition);
                }
            }
        }

        public override void Update()
        {
            base.Update();
            if (!isEnemyDead && !GrabStickIfNotHolding())
            {
                return;
            }

            if (isEnemyDead)
            {
                // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
                if (!isDeadAnimationDone)
                {
                    isDeadAnimationDone = true;
                    StartCoroutine(MuteWithDelay(2));
                    DoAnimationClientRpc("killEnemy");
                    PlayDeathSoundClientRpc();
                }
                return;
            }

            timeSinceNewRandPos += Time.deltaTime;
            var state = currentBehaviourStateIndex;
            if (targetPlayer != null && (state == (int)State.ChasePlayer || state == (int)State.AttackPlayer))
            {
                //SetDestinationToPosition(targetPlayer.transform.position);

                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 8f * Time.deltaTime);
            }

            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
            }

            if (timeSinceHittingPlayer > 0f)
            {
                timeSinceHittingPlayer -= Time.deltaTime;

                if(timeSinceHittingPlayer <= 0f)
                {
                    LogIfDebugBuild("can chase player again");
                }
            }

            if(searchTimer > 0f && state == (int)State.SearchingForPlayer)
            {
                searchTimer -= Time.deltaTime;
            }
        }

        public override void OnGainedOwnership()
        {
            base.OnGainedOwnership();

            if (IsOwner)
            {
                LogIfDebugBuild("Get ownership");
                if (currentBehaviourStateIndex == (int)State.SearchingForPlayer)
                {
                    LogIfDebugBuild("restart search coroutine");

                    DoAnimationClientRpc("startWalk");
                    searchTimer = 10f;
                    StartSearch(transform.position);
                }

                if (currentBehaviourStateIndex == (int)State.ChasePlayer)
                {
                    FoundClosestPlayerInRange(10f, 5f);
                }
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    agent.speed = 18f;

                    if (!IsServer && IsOwner)
                    {
                        LogIfDebugBuild("Set Ownership back to : " + StartOfRound.Instance.allPlayerScripts[0].playerUsername);
                        //ChangeOwnershipOfEnemy(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
                        return;
                    }

                    if (FoundClosestPlayerInRange(10f, 5f) && targetPlayer != null && timeSinceHittingPlayer <= 0f)
                    {
                        LogIfDebugBuild("chasing Player : " + targetPlayer.playerUsername);
                        SwitchToBehaviourState((int)State.ChasePlayer);
                    }

                    if(searchTimer <= 0f)
                    {
                        searchTimer = 10f;
                        StartSearch(transform.position);
                    }

                    break;
                case (int)State.ChasePlayer:
                    agent.speed = 13f;

                    if (targetPlayer == null)
                        FoundClosestPlayerInRange(10f, 5f);

                    if (IsOwner && targetPlayer != null && targetPlayer != GameNetworkManager.Instance.localPlayerController)
                    {
                       // ChangeOwnershipOfEnemy(targetPlayer.actualClientId);
                    }

                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (!TargetClosestPlayerInAnyCase() || targetPlayer == null || targetPlayer.inSpecialInteractAnimation == true || Vector3.Distance(transform.position, targetPlayer.transform.position) > 18)
                    {
                        LogIfDebugBuild("Stop Target Player");
                        if (targetDebugInstance != null)
                            Destroy(targetDebugInstance);

                        searchTimer = 10f;
                        StartSearch(transform.position);
                        DoAnimationClientRpc("startWalk");
                        SwitchToBehaviourState((int)State.SearchingForPlayer);
                        return;
                    }

                    StickingInFrontOfPlayer();

                    if (CheckLineOfSightForPosition(targetPlayer.transform.position) && !attackingPlayer && timeSinceHittingPlayer <= 0f)
                    {
                        StartCoroutine(SwingAttack());
                    }

                    break;
                case (int)State.AttackPlayer:
                    agent.speed = 0f;
                    break;
                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        }

        bool FoundClosestPlayerInRange(float range, float senseRange)
        {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if (targetPlayer == null)
            {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }

            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }

        bool TargetClosestPlayerInAnyCase()
        {
            mostOptimalDistance = 2000f;
            targetPlayer = null;
            for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
            {
                tempDist = Vector3.Distance(transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
                if (tempDist < mostOptimalDistance)
                {
                    mostOptimalDistance = tempDist;
                    targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
                }
            }
            if (targetPlayer == null) return false;
            return true;
        }

        void StickingInFrontOfPlayer()
        {
            if (targetPlayer == null)
                return;

            if (targetDebugInstance != null)
                Destroy(targetDebugInstance);

            positionRandomness = new Vector3(enemyRandom.Next(-2, 2), 0, enemyRandom.Next(-2, 2));

            if (!CheckLineOfSightForPosition(targetPlayer.transform.position))
            {
                stalkPos = targetPlayer.transform.position;
            }
            else stalkPos = targetPlayer.transform.position + (7 * (transform.position - targetPlayer.transform.position).normalized) + positionRandomness;

            //targetDebugInstance = Instantiate(targetDebug, transform.parent);
            //targetDebugInstance.transform.position = stalkPos;

            SetDestinationToPosition(stalkPos);
        }

        IEnumerator SwingAttack()
        {
            LogIfDebugBuild("Try swing attack");
            timeSinceHittingPlayer = 2f;

            SwitchToBehaviourClientRpc((int)State.AttackPlayer);
            attackingPlayer = true;

            DoAnimationClientRpc("attack");
            yield return new WaitForSeconds(0.3f);

            if (targetPlayer != null)
            {
                transform.LookAt(targetPlayer.transform.position + new Vector3(0, 0.5f, 0));
                LaunchFireballClientRpc((int)targetPlayer.actualClientId);
            }          

            yield return new WaitForSeconds(0.1f);

            attackingPlayer = false;

            //searchTimer = 10f;
            //StartSearch(transform.position);
            //DoAnimationClientRpc("startWalk");
            //SwitchToBehaviourState((int)State.SearchingForPlayer);

            LogIfDebugBuild("chasing Player : " + targetPlayer.playerUsername);
            DoAnimationClientRpc("startWalk");
            SwitchToBehaviourState((int)State.ChasePlayer);
        }

        [ClientRpc]
        public void LaunchFireballClientRpc(int playerId)
        {
            LogIfDebugBuild("fireball instance");
            if (fireballInstance != null)
            {
                if(!launchSpellFx.isEmitting)
                    launchSpellFx.Play();

                fireballInstance.transform.parent = transform.parent;
                fireballInstance.transform.position = launchSpellFx.transform.position;
                fireballInstance.transform.rotation = transform.rotation;

                LogIfDebugBuild("playerTest : " + (targetPlayer != null));

                fireballInstance.GetComponent<MagicSpell>().Launch(playerId);
            }
            CreateProjectileServerRpc();
        }

        [ServerRpc]
        public void CreateProjectileServerRpc()
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }

            if (__rpc_exec_stage == __RpcExecStage.Server && networkManager.IsServer || networkManager.IsHost)
            {
                serverFireballInstance = Instantiate(fireballPrefab, transform);
                serverFireballInstance.GetComponent<NetworkObject>().SpawnWithOwnership(base.OwnerClientId);
                serverFireballInstance.transform.position = Vector3.zero;

                CreateProjectileClientRpc();
            }
        }

        [ClientRpc]
        public void CreateProjectileClientRpc()
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && networkManager.IsClient || networkManager.IsHost)
            {
                fireballInstance = serverFireballInstance;
            }
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);

            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other, attackingPlayer);
            if (playerControllerB != null)
            {
                if (IsOwner)
                {
                    //LogIfDebugBuild("Change ownership");
                   //ChangeOwnershipOfEnemy(playerControllerB.actualClientId);
                }
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            base.KillEnemy();

            if (creatureVoice != null)
            {
                creatureVoice.Stop();
            }

            if (base.IsOwner)
            {
                DropStickServerRpc(dropSpot.position);
            }

            fireAura.SetActive(false);
            if (fireballInstance != null)
            {
                fireballInstance.SetActive(false);
            }           

            creatureSFX.Stop();
            creatureAnimator.SetLayerWeight(2, 0f);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead)
            {
                return;
            }

            enemyHP -= force;
            if (IsOwner)
            {
                if (enemyHP <= 0)
                {
                    StopCoroutine(searchCoroutine);
                    KillEnemyOnOwnerClient();
                }
            }
        }

        [ClientRpc]
        public void PlayDeathSoundClientRpc()
        {
            creatureSFX.PlayOneShot(dieSFX);
        }

        IEnumerator MuteWithDelay(float delay)
        {
            MuteVoiceClientRpc(true);

            yield return new WaitForSeconds(delay);

            MuteSfxClientRpc(true);
        }

        [ClientRpc]
        public void MuteSfxClientRpc(bool mute)
        {
            LogIfDebugBuild("Mute Sfx : " + mute);
            creatureSFX.mute = mute;
        }

        [ClientRpc]
        public void MuteVoiceClientRpc(bool mute)
        {
            LogIfDebugBuild("Mute Voice : " + mute);
            creatureVoice.mute = mute;
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }
    }
}


