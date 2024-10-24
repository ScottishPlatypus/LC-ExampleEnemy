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

    class BrolyAI : EnemyAI
    {
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
        public Transform assParent = null!;
        public Transform playerParent = null!;
        public AudioSource stepSound = null!;
#pragma warning restore 0649
        float timeSinceHittingLocalPlayer;
        float timeSinceNewRandPos;
        Vector3 positionRandomness;
        Vector3 spawnPos;
        System.Random enemyRandom = null!;
        bool isDeadAnimationDone;
        bool attackingPlayer;
        bool isInterrupted;
        float fleaTimer;
        Coroutine attackingPlayerCoroutine;
        private Ray playerRay;
        public bool carryingPlayerBody;
        Vector3 targetLastPos;

        public DeadBodyInfo bodyBeingCarried;

        FlowermanAI flowermanTarget = null!;
        enum State
        {
            SearchingForPlayer,
            ChasePlayer,
            ChaseBracken,
            AttackPlayer,
            AttackBracken,
            Flea,
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start()
        {
            base.Start();
            LogIfDebugBuild("Broly Spawned");
            LogIfDebugBuild("Is Host : " + IsServer);
            timeSinceHittingLocalPlayer = 0;
            timeSinceNewRandPos = 0;
            DoAnimationClientRpc("startWalk");
            positionRandomness = new Vector3(0, 0, 0);
            agent.acceleration = 15f;
            agent.angularSpeed = 1000f;
            spawnPos = transform.position;
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;
            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            MuteVoiceClientRpc(true);
            MuteStepsClientRpc(false);
            currentBehaviourStateIndex = (int)State.SearchingForPlayer;
            // We make the enemy start searching. This will make it start wandering around.
            StartSearch(transform.position);
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead)
            {
                // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
                if (!isDeadAnimationDone)
                {
                    LogIfDebugBuild("Stopping enemy voice with janky code.");
                    isDeadAnimationDone = true;
                    MuteVoiceClientRpc(true);
                    MuteStepsClientRpc(true);
                    DoAnimationClientRpc("killEnemy");
                    PlayDeathSoundClientRpc();
                }
                return;
            }

            var state = currentBehaviourStateIndex;
            if (targetPlayer != null && state == (int)State.ChasePlayer)
            {
                SetDestinationToPosition(targetPlayer.transform.position);
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);

                if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 2)
                {
                    transform.position = Vector3.MoveTowards(transform.position, targetPlayer.transform.position, 3f * Time.deltaTime);
                }
            }

            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
            }

            if (fleaTimer > 0f)
            {
                fleaTimer -= Time.deltaTime;
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
                    MuteVoiceClientRpc(true);
                    MuteStepsClientRpc(false);
                    StartSearch(transform.position);
                }

                if (currentBehaviourStateIndex == (int)State.ChasePlayer)
                {
                    FoundClosestPlayerInRange(20f, 4f);
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
                    agent.speed = 2.5f;

                    if (!IsServer && IsOwner)
                    {
                        LogIfDebugBuild("Set Ownership back to : " + StartOfRound.Instance.allPlayerScripts[0].playerUsername);
                        ChangeOwnershipOfEnemy(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
                        return;
                    }

                    if (FoundBrackenInMap() && flowermanTarget != null)
                    {
                        LogIfDebugBuild("found bracken");
                        DoAnimationClientRpc("chaseBracken");
                        SwitchToBehaviourState((int)State.ChaseBracken);
                        MuteStepsClientRpc(true);
                        MuteVoiceClientRpc(true);
                    }
                    else if (FoundClosestPlayerInRange(20f, 4f) && targetPlayer != null)
                    {
                        LogIfDebugBuild("chasing Player : " + targetPlayer.playerUsername);
                        DoAnimationClientRpc("chasePlayer");
                        SwitchToBehaviourState((int)State.ChasePlayer);
                        MuteStepsClientRpc(true);
                        MuteVoiceClientRpc(false);
                    }

                    break;
                case (int)State.ChasePlayer:
                    agent.speed = 8f;

                    if (targetPlayer == null)
                        FoundClosestPlayerInRange(20f, 4f);


                    if (IsOwner && targetPlayer != null && targetPlayer != GameNetworkManager.Instance.localPlayerController)
                    {
                        ChangeOwnershipOfEnemy(targetPlayer.actualClientId);
                    }

                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (targetPlayer == null || FoundBrackenInMap() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 20 && !CheckLineOfSightForPosition(targetPlayer.transform.position)))
                    {
                        LogIfDebugBuild("Stop Target Player");
                        StartSearch(transform.position);
                        DoAnimationClientRpc("startWalk");
                        SwitchToBehaviourState((int)State.SearchingForPlayer);
                        MuteStepsClientRpc(false);
                        MuteVoiceClientRpc(true);
                        return;
                    }

                    //SetDestinationToPosition(targetPlayer.transform.position);

                    break;
                case (int)State.ChaseBracken:
                    agent.speed = 15f;
                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (flowermanTarget == null || flowermanTarget.isEnemyDead)
                    {
                        LogIfDebugBuild("Stop Target Bracken");
                        StartSearch(transform.position);
                        DoAnimationClientRpc("startWalk");
                        SwitchToBehaviourState((int)State.SearchingForPlayer);
                        MuteVoiceClientRpc(true);
                        MuteStepsClientRpc(false);
                        return;
                    }

                    SetDestinationToPosition(flowermanTarget.transform.position, checkForPath: false);
                    break;

                case (int)State.AttackPlayer:
                    break;
                case (int)State.AttackBracken:
                    break;

                case (int)State.Flea:
                    agent.speed = 15f;

                    if (fleaTimer <= 0 || (Vector3.Distance(transform.position, spawnPos) < 1))
                    {
                        fleaTimer = 0;
                        LogIfDebugBuild("Stop fleeing");
                        if (carryingPlayerBody)
                        {
                            DropPlayerBody();
                            DropPlayerBodyServerRpc();
                        }

                        StartSearch(transform.position);
                        DoAnimationClientRpc("startWalk");
                        SwitchToBehaviourState((int)State.SearchingForPlayer);

                        MuteVoiceClientRpc(true);
                        MuteStepsClientRpc(false);
                    }
                    // We don't care about doing anything here
                    break;

                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        }

        bool FoundBrackenInMap()
        {
            flowermanTarget = null;
            mostOptimalDistance = 2000f;
            if (FindObjectsOfType<FlowermanAI>().Length > 0)
            {
                for (int i = 0; i < FindObjectsOfType<FlowermanAI>().Length; i++)
                {
                    if (!FindObjectsOfType<FlowermanAI>()[i].isEnemyDead && Vector3.Distance(transform.position, FindObjectsOfType<FlowermanAI>()[i].transform.position) < mostOptimalDistance)
                    {
                        flowermanTarget = FindObjectsOfType<FlowermanAI>()[i];
                        mostOptimalDistance = Vector3.Distance(transform.position, FindObjectsOfType<FlowermanAI>()[i].transform.position);
                        break;
                    }
                }

                return flowermanTarget != null;
            }
            else return false;
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

        [ServerRpc]
        public void DropPlayerBodyServerRpc()
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
                        LogIfDebugBuild("Only the owner can invoke a ServerRpc that requires ownership!");
                    }
                    return;
                }
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                DropPlayerBodyClientRpc();
            }
        }

        [ClientRpc]
        public void DropPlayerBodyClientRpc()
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager != null && networkManager.IsListening)
            {
                if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
                {
                    DropPlayerBody();
                }
            }
        }

        private void DropPlayerBody()
        {
            if (carryingPlayerBody)
            {
                LogIfDebugBuild("Drop player body : " + bodyBeingCarried.playerScript.playerUsername);
                carryingPlayerBody = false;
                bodyBeingCarried.matchPositionExactly = false;
                bodyBeingCarried.attachedTo = null;
                bodyBeingCarried = null;
            }
        }

        IEnumerator FleaPlayer(bool carryingBody = true)
        {
            LogIfDebugBuild("Flea player");

            if (attackingPlayerCoroutine != null)
            {
                StopCoroutine(attackingPlayerCoroutine);
            }

            inSpecialAnimation = false;
            attackingPlayer = false;
            DoAnimationClientRpc("startWalk");

            MuteVoiceClientRpc(true);
            MuteStepsClientRpc(true);
            fleaTimer = 8f;

            if (inSpecialAnimationWithPlayer != null)
            {
                LogIfDebugBuild("Release player : " + inSpecialAnimationWithPlayer.playerUsername);
                inSpecialAnimationWithPlayer.inSpecialInteractAnimation = false;
                inSpecialAnimationWithPlayer.snapToServerPosition = false;
                inSpecialAnimationWithPlayer.inAnimationWithEnemy = null;
                if (carryingBody && inSpecialAnimationWithPlayer.deadBody != null)
                {
                    bodyBeingCarried = inSpecialAnimationWithPlayer.deadBody;
                    bodyBeingCarried.attachedTo = assParent;
                    bodyBeingCarried.attachedLimb = inSpecialAnimationWithPlayer.deadBody.bodyParts[0];
                    bodyBeingCarried.matchPositionExactly = true;
                    carryingPlayerBody = true;
                }
            }

            if(carryingBody)
                yield return new WaitForSeconds(1f);

            if (IsOwner)
            {
                agent.enabled = true;
                isClientCalculatingAI = true;
            }

            SwitchToBehaviourState((int)State.Flea);
            SetDestinationToPosition(spawnPos);
        }

        [ServerRpc]
        void AttackPlayerServerRpc(int playerObjectId)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                if (!attackingPlayer && !carryingPlayerBody)
                {
                    isInterrupted = false;
                    attackingPlayer = true;
                    inSpecialAnimation = true;
                    isClientCalculatingAI = false;
                    inSpecialAnimationWithPlayer = StartOfRound.Instance.allPlayerScripts[playerObjectId];
                    inSpecialAnimationWithPlayer.inAnimationWithEnemy = this;
                    transform.position = new Vector3(inSpecialAnimationWithPlayer.transform.position.x, inSpecialAnimationWithPlayer.transform.position.y + 0.5f, inSpecialAnimationWithPlayer.transform.position.z);
                    AttackPlayerClientRpc(playerObjectId);
                }
            }
        }

        [ClientRpc]
        void AttackPlayerClientRpc(int playerObjectId)
        {
            LogIfDebugBuild("attack CLIENT rpc"); 

            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
                LogIfDebugBuild("attack test");
                inSpecialAnimationWithPlayer = StartOfRound.Instance.allPlayerScripts[playerObjectId];
                inSpecialAnimationWithPlayer.inAnimationWithEnemy = this;
                //inSpecialAnimationWithPlayer.transform.position = playerParent.transform.position;
                transform.position = new Vector3(inSpecialAnimationWithPlayer.transform.position.x, inSpecialAnimationWithPlayer.transform.position.y + 0.5f, inSpecialAnimationWithPlayer.transform.position.z);
                SyncPositionToClients();
                inSpecialAnimationWithPlayer.SyncBodyPositionWithClients();
                inSpecialAnimationWithPlayer.DropAllHeldItems();
                inSpecialAnimationWithPlayer.voiceMuffledByEnemy = true;
                attackingPlayer = true;
                inSpecialAnimation = true;
                agent.enabled = false;
                inSpecialAnimationWithPlayer.inSpecialInteractAnimation = true;
                inSpecialAnimationWithPlayer.snapToServerPosition = true;
                Vector3 vector = ((!inSpecialAnimationWithPlayer.IsOwner) ? inSpecialAnimationWithPlayer.transform.parent.TransformPoint(inSpecialAnimationWithPlayer.serverPlayerPosition) : inSpecialAnimationWithPlayer.transform.position);
                Vector3 position = base.transform.position;
                position.y = inSpecialAnimationWithPlayer.transform.position.y;
                playerRay = new Ray(vector, position - inSpecialAnimationWithPlayer.transform.position);
                turnCompass.LookAt(vector);
                position = base.transform.eulerAngles;
                position.y = turnCompass.eulerAngles.y;
                base.transform.eulerAngles = position;
                if (attackingPlayerCoroutine != null)
                {
                    StopCoroutine(attackingPlayerCoroutine);
                }
                attackingPlayerCoroutine = StartCoroutine(AttackPlayer());
            }
        }

        IEnumerator AttackPlayer()
        {
            Vector3 endPosition = playerRay.GetPoint(1f);
            if (endPosition.y < -80f)
            {
                Vector3 startPosition = base.transform.position;
                for (int i = 0; i < 15; i++)
                {
                    //  base.transform.position = Vector3.Lerp(startPosition, endPosition, (float)i / 5f);
                    yield return null;
                }
                base.transform.position = endPosition;
            }

            DoAnimationClientRpc("attack");
            SwitchToBehaviourState((int)State.AttackPlayer);
            MuteStepsClientRpc(true);
            MuteVoiceClientRpc(true);

            int hitCount = 0;
            if (inSpecialAnimationWithPlayer != null)
            {
                while (hitCount < 10 || inSpecialAnimationWithPlayer.deadBody == null || !isInterrupted)
                {
                    yield return new WaitForSeconds(0.5f);

                    inSpecialAnimationWithPlayer.DamagePlayer(15);
                    hitCount++;

                    LogIfDebugBuild("attacking : " + inSpecialAnimationWithPlayer.playerUsername + " - Hp : " + inSpecialAnimationWithPlayer.health + " - HitCount : " + hitCount);
                    if ((hitCount >= 10 || inSpecialAnimationWithPlayer.deadBody != null || isInterrupted))
                        break;

                    yield return null;
                }

                if (inSpecialAnimationWithPlayer == null || inSpecialAnimationWithPlayer.deadBody == null)
                {
                   //LogIfDebugBuild("Flowerman: Player body was not spawned or found within 2 seconds.");
                    StartCoroutine(FleaPlayer(carryingBody: false));
                }
                else
                {
                    inSpecialAnimationWithPlayer.snapToServerPosition = false;
                    inSpecialAnimationWithPlayer.deadBody.causeOfDeath = CauseOfDeath.Suffocation;
                    inSpecialAnimationWithPlayer.deadBody.bodyBleedingHeavily = true;
                    StartCoroutine(FleaPlayer());
                }
            }
        }

        [ServerRpc]
        void AttackBrackenServerRpc(int flowermanId)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                AttackBrackenClientRpc(flowermanId);
            }
        }

        [ClientRpc]
        void AttackBrackenClientRpc(int flowermanId)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
               // AttackBracken(flowermanId);
            }
        }

        IEnumerator AttackBracken()
        {
            LogIfDebugBuild("Attack Bracken");
            SwitchToBehaviourClientRpc((int)State.AttackBracken);
            MuteStepsClientRpc(true);
            MuteVoiceClientRpc(false);

           // FlowermanAI flowerman = FindObjectsOfType<FlowermanAI>()[flowermanId];

            //flowerman.KillEnemyOnOwnerClient();

            yield return new WaitForSeconds(4f);

            StartSearch(transform.position);
            DoAnimationClientRpc("attack");
            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            MuteVoiceClientRpc(true);
            MuteStepsClientRpc(false);
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null)
        {
            base.OnCollideWithEnemy(other, collidedEnemy);

            FlowermanAI bracken = other.GetComponentInParent<FlowermanAI>();
            if (bracken != null && !bracken.isEnemyDead)
            {
                LogIfDebugBuild("Broly Collision with Bracken!");

                bracken.KillEnemyOnOwnerClient();
                AttackBracken();
               // AttackBrackenServerRpc(bracken.GetInstanceID());
            }
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);

            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other, attackingPlayer || carryingPlayerBody || fleaTimer > 0);
            if (playerControllerB != null)
            {
                LogIfDebugBuild("Broly Collision with Player!");
                if (!IsOwner)
                {
                    //LogIfDebugBuild("Change ownership");
                   //ChangeOwnershipOfEnemy(playerControllerB.actualClientId);
                }
                else
                {
                    AttackPlayerServerRpc((int)playerControllerB.playerClientId);
                }
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            if (creatureVoice != null)
            {
                creatureVoice.Stop();
            }
            creatureSFX.Stop();
            stepSound.Stop();
            creatureAnimator.SetLayerWeight(2, 0f);
            base.KillEnemy();
            if (carryingPlayerBody)
            {
                carryingPlayerBody = false;
                if (bodyBeingCarried != null)
                {
                    bodyBeingCarried.matchPositionExactly = false;
                    bodyBeingCarried.attachedTo = null;
                }
            }
            if (attackingPlayer && inSpecialAnimationWithPlayer != null)
            {
                inSpecialAnimationWithPlayer.inSpecialInteractAnimation = false;
                inSpecialAnimationWithPlayer.snapToServerPosition = false;
                inSpecialAnimationWithPlayer.voiceMuffledByEnemy = false;
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead)
            {
                return;
            }

            if (!isInterrupted)
            {
                LogIfDebugBuild("Interrupt enemy");
                isInterrupted = true;
            }

            if (currentBehaviourStateIndex == (int)State.SearchingForPlayer && playerWhoHit != null)
            {
                LogIfDebugBuild("Chase " + playerWhoHit + " after being hit");
                targetPlayer = playerWhoHit;
                DoAnimationClientRpc("chasePlayer");
                SwitchToBehaviourClientRpc((int)State.ChasePlayer);
                MuteVoiceClientRpc(false);
                MuteStepsClientRpc(true);
            }

            enemyHP -= force;
            if (IsOwner)
            {


                if (enemyHP <= 0)
                {
                    StopCoroutine(searchCoroutine);
                    if(attackingPlayerCoroutine != null)
                        StopCoroutine(attackingPlayerCoroutine);

                    MuteVoiceClientRpc(false);
                    MuteStepsClientRpc(true);
                    KillEnemyOnOwnerClient();
                }
            }
        }

        [ServerRpc]
        public void StartSearchServerRpc(Vector3 pos)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                StartSearchClientRpc(pos);
            }
        }

        [ClientRpc]
        public void StartSearchClientRpc(Vector3 pos)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
                StartSearch(pos);
            }
        }

        [ServerRpc]
        public void SetTargetPlayerServerRpc(int playerObjectId)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                SetTargetPlayerClientRpc(playerObjectId);
            }
        }

        [ClientRpc]
        public void SetTargetPlayerClientRpc(int playerObjectId)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
                targetPlayer = StartOfRound.Instance.allPlayerScripts[playerObjectId];
            }
        }

        [ClientRpc]
        public void PlayDeathSoundClientRpc()
        {
            creatureSFX.PlayOneShot(dieSFX);
        }

        [ClientRpc]
        public void MuteVoiceClientRpc(bool mute)
        {
            LogIfDebugBuild("Mute Voice : " + mute);
            creatureVoice.mute = mute;
        }

        [ClientRpc]
        public void MuteStepsClientRpc(bool mute)
        {
            LogIfDebugBuild("Mute Steps : " + mute);
            stepSound.mute = mute;
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }
    }
}


