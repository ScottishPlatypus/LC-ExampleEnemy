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

    class RandyOrtonAI : EnemyAI
    {
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
#pragma warning restore 0649
        float timeSinceHittingLocalPlayer;
        float timeSinceNewRandPos;
        Vector3 positionRandomness;
        Vector3 StalkPos;
        System.Random enemyRandom = null!;
        bool isDeadAnimationDone;
        bool isAgressive;
        float puntKickTimer;
        enum State {
            SearchingForPlayer,
            ChasePlayerRko,
            ChasePlayerPuntKick,
            RkoInProgress,
            PuntKickInProgress,
            PuntKickNoSound,
            Pin,
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start() {
            base.Start();
            LogIfDebugBuild("Example Enemy Spawned");
            timeSinceHittingLocalPlayer = 0;
            creatureVoice.mute = false;
            DoAnimationClientRpc("startWalk");
            timeSinceNewRandPos = 0;
            positionRandomness = new Vector3(0, 0, 0);
            agent.acceleration = 15f;
            agent.angularSpeed = 360f;
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;
            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            currentBehaviourStateIndex = (int)State.SearchingForPlayer;
            // We make the enemy start searching. This will make it start wandering around.
            StartSearch(transform.position);
        }

        public override void Update() {
            base.Update();
            if (isEnemyDead) {
                // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
                if (!isDeadAnimationDone) {
                    LogIfDebugBuild("Stopping enemy voice with janky code.");
                    isDeadAnimationDone = true;
                    MuteVoiceClientRpc(false);
                    PlayDeathSoundClientRpc();
                }
                return;
            }
            timeSinceHittingLocalPlayer += Time.deltaTime;
            timeSinceNewRandPos += Time.deltaTime;

            var state = currentBehaviourStateIndex;
            if (targetPlayer != null && (state == (int)State.ChasePlayerRko || state == (int)State.ChasePlayerPuntKick)) {
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
            }
            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
            }

            if(puntKickTimer > 0f)
            {
                puntKickTimer -= Time.deltaTime;
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
                    StartSearch(transform.position);
                    SwitchToBehaviourState((int)State.SearchingForPlayer);
                    MuteVoiceClientRpc(false);
                    DoAnimationClientRpc("startWalk");
                }

                if (currentBehaviourStateIndex == (int)State.ChasePlayerRko || currentBehaviourStateIndex == (int)State.ChasePlayerPuntKick)
                {
                    FoundClosestPlayerInRange(15f, 5f);
                }
            }
        }

        public override void DoAIInterval() {

            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            };

            switch (currentBehaviourStateIndex) {
                case (int)State.SearchingForPlayer:
                    agent.speed = 3f;

                    if (!IsServer && IsOwner)
                    {
                        LogIfDebugBuild("Set Ownership back to : " + StartOfRound.Instance.allPlayerScripts[0].playerUsername);
                        ChangeOwnershipOfEnemy(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
                        return;
                    }

                    if (FoundClosestPlayerInRange(15f, 10f) && targetPlayer != null) {
                        if (targetPlayer.health <= 90 || isAgressive)
                        {
                            LogIfDebugBuild("Start Target Player For PuntKick");
                            StopSearch(currentSearch);
                            MuteVoiceClientRpc(true);

                            SwitchToBehaviourState((int)State.ChasePlayerPuntKick);
                            DoAnimationClientRpc("puntKickChase");
                            return;
                        }
                        else if (!targetPlayer.HasLineOfSightToPosition(transform.position))
                        {
                            LogIfDebugBuild("Start Target Player For Rko");
                            StopSearch(currentSearch);
                            MuteVoiceClientRpc(true);
                            SwitchToBehaviourState((int)State.ChasePlayerRko);
                            DoAnimationClientRpc("rkoChase");
                            return;
                        }
                    }
                    break;

                case (int)State.ChasePlayerRko:
                    agent.speed = 7f;

                    if (targetPlayer == null)
                        FoundClosestPlayerInRange(15f, 5f);


                    if (IsOwner && targetPlayer != null && targetPlayer != GameNetworkManager.Instance.localPlayerController)
                    {
                        ChangeOwnershipOfEnemy(targetPlayer.actualClientId);
                    }

                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (targetPlayer == null || targetPlayer.HasLineOfSightToPosition(transform.position) == true || Vector3.Distance(transform.position, targetPlayer.transform.position) > 15f) {
                        LogIfDebugBuild("Stop Target Player");
                        StartSearch(transform.position);
                        SwitchToBehaviourState((int)State.SearchingForPlayer);
                        MuteVoiceClientRpc(false);
                        DoAnimationClientRpc("startWalk");
                        return;
                    }

                    if (targetPlayer.health <= 90 || isAgressive)
                    {
                        LogIfDebugBuild("Player is down under 50 hp");
                        SwitchToBehaviourState((int)State.ChasePlayerPuntKick);
                        DoAnimationClientRpc("puntKickChase");
                        return;
                    }

                    SetDestinationToPosition(targetPlayer.transform.position);

                    if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 1f)
                    {
                        RkoAttackServerRpc((int)targetPlayer.actualClientId);
                    }

                    break;
                case (int)State.ChasePlayerPuntKick:
                    agent.speed = 15f;

                    if (targetPlayer == null)
                        FoundClosestPlayerInRange(15f, 5f);

                    if (IsOwner && targetPlayer != null && targetPlayer != GameNetworkManager.Instance.localPlayerController)
                    {
                        ChangeOwnershipOfEnemy(targetPlayer.actualClientId);
                    }

                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (targetPlayer == null || Vector3.Distance(transform.position, targetPlayer.transform.position) > 15 || (targetPlayer.health > 90 && !isAgressive))
                    {
                        LogIfDebugBuild("Stop Target Player");
                        isAgressive = false;
                        StartSearch(transform.position);
                        SwitchToBehaviourState((int)State.SearchingForPlayer);
                        MuteVoiceClientRpc(false);
                        DoAnimationClientRpc("startWalk");
                        return;
                    }

                    SetDestinationToPosition(targetPlayer.transform.position);

                    if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 3f)
                    {
                        PuntKickServerRpc((int)targetPlayer.actualClientId);
                    }

                    break;

                case (int)State.RkoInProgress:
                    agent.speed = 0f;
                    // We don't care about doing anything here
                    break;

                case (int)State.PuntKickInProgress:
                case (int)State.PuntKickNoSound:
                    agent.speed = 2f;
                    // We don't care about doing anything here
                    break;

                case (int)State.Pin:
                    agent.speed = 0f;
                    // We don't care about doing anything here
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

        public override void OnCollideWithPlayer(Collider other) {
            if (timeSinceHittingLocalPlayer < 1f) {
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null && playerControllerB != targetPlayer)
            {
                //LogIfDebugBuild("Example Enemy Collision with Player!");
               // timeSinceHittingLocalPlayer = 0f;
               // playerControllerB.DamagePlayer(100);
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1) {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if(isEnemyDead){
                return;
            }
            enemyHP -= force;
            if (IsOwner) {
                if (!isAgressive)
                {
                    isAgressive = true;
                    StopSearch(currentSearch);
                    SwitchToBehaviourState((int)State.ChasePlayerPuntKick);
                    DoAnimationClientRpc("puntKickChase");
                }
              
                if (enemyHP <= 0 && !isEnemyDead) {
                    // Our death sound will be played through creatureVoice when KillEnemy() is called.
                    // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                    // so we don't need to call a death animation ourselves.
                    isAgressive = false;
                    StopCoroutine(RkoAttack());
                    StopCoroutine(PuntKick());
                    // We need to stop our search coroutine, because the game does not do that by default.
                    StopCoroutine(searchCoroutine);
                    KillEnemyOnOwnerClient();
                }
            }
        }

        [ClientRpc]
        public void PlayDeathSoundClientRpc()
        {
            creatureVoice.Stop();
            creatureVoice.PlayOneShot(dieSFX);
        }

        [ClientRpc]
        public void MuteVoiceClientRpc(bool mute)
        {
            LogIfDebugBuild("Mute voice : " + mute);
            creatureVoice.mute = mute;
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName) {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

        [ServerRpc]
        void RkoAttackServerRpc(int playerObjectId)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                inSpecialAnimation = true;
                isClientCalculatingAI = false;
                inSpecialAnimationWithPlayer = StartOfRound.Instance.allPlayerScripts[playerObjectId];
                inSpecialAnimationWithPlayer.inAnimationWithEnemy = this;
                transform.position = new Vector3(inSpecialAnimationWithPlayer.transform.position.x, inSpecialAnimationWithPlayer.transform.position.y + 0.5f, inSpecialAnimationWithPlayer.transform.position.z);
                RkoAttackClientRpc(playerObjectId);
            }
        }

        [ClientRpc]
        void RkoAttackClientRpc(int playerObjectId)
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
                inSpecialAnimationWithPlayer.voiceMuffledByEnemy = true;
                inSpecialAnimation = true;
                agent.enabled = false;
                inSpecialAnimationWithPlayer.inSpecialInteractAnimation = true;
                inSpecialAnimationWithPlayer.snapToServerPosition = true;
                Vector3 vector = ((!inSpecialAnimationWithPlayer.IsOwner) ? inSpecialAnimationWithPlayer.transform.parent.TransformPoint(inSpecialAnimationWithPlayer.serverPlayerPosition) : inSpecialAnimationWithPlayer.transform.position);
                Vector3 position = base.transform.position;
                position.y = inSpecialAnimationWithPlayer.transform.position.y;
                turnCompass.LookAt(vector);
                position = base.transform.eulerAngles;
                position.y = turnCompass.eulerAngles.y;
                base.transform.eulerAngles = position;
                StartCoroutine(RkoAttack());
            }
        }

        IEnumerator RkoAttack()
        {
            SwitchToBehaviourState((int)State.RkoInProgress);
            SetDestinationToPosition(inSpecialAnimationWithPlayer.transform.position);
            DoAnimationClientRpc("rko");
            yield return new WaitForSeconds(0.5f);
         
            if (inSpecialAnimationWithPlayer != null)
            {
                LogIfDebugBuild("Rko hit player!");
                inSpecialAnimationWithPlayer.DamagePlayer(400);
                yield return new WaitForSeconds(1.2f);

                if (inSpecialAnimationWithPlayer != null && inSpecialAnimationWithPlayer.deadBody != null)
                {
                    inSpecialAnimationWithPlayer.snapToServerPosition = false;
                    inSpecialAnimationWithPlayer.deadBody.causeOfDeath = CauseOfDeath.Gravity;
                    inSpecialAnimationWithPlayer.deadBody.bodyBleedingHeavily = true;

                    DoAnimationClientRpc("pin");
                    SwitchToBehaviourState((int)State.Pin);

                    yield return new WaitForSeconds(3f);
                }
            }

            inSpecialAnimation = false;

            StartSearch(transform.position);
            SwitchToBehaviourState((int)State.SearchingForPlayer);
            creatureVoice.mute = false;
            DoAnimationClientRpc("startWalk");
        }

        [ServerRpc]
        void PuntKickServerRpc(int playerObjectId)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                inSpecialAnimationWithPlayer = StartOfRound.Instance.allPlayerScripts[playerObjectId];
                PuntKickClientRpc(playerObjectId);
            }
        }

        [ClientRpc]
        void PuntKickClientRpc(int playerObjectId)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
                inSpecialAnimationWithPlayer = StartOfRound.Instance.allPlayerScripts[playerObjectId];
                StartCoroutine(PuntKick());
            }
        }

        IEnumerator PuntKick()
        {
            if (puntKickTimer <= 0f)
            {
                SwitchToBehaviourState((int)State.PuntKickInProgress);
                puntKickTimer = 7f;
            }
            else SwitchToBehaviourState((int)State.PuntKickNoSound);

            DoAnimationClientRpc("puntKick");
            yield return new WaitForSeconds(0.2f);

            if (inSpecialAnimationWithPlayer != null && Vector3.Distance(transform.position, inSpecialAnimationWithPlayer.transform.position) < 1.5f)
            {
                    LogIfDebugBuild("PuntKick hit player!");
                    inSpecialAnimationWithPlayer.DamagePlayer(400);
                    yield return new WaitForSeconds(1.2f);

                    if (inSpecialAnimationWithPlayer != null && inSpecialAnimationWithPlayer.deadBody != null)
                    {
                        inSpecialAnimationWithPlayer.snapToServerPosition = false;
                        inSpecialAnimationWithPlayer.deadBody.causeOfDeath = CauseOfDeath.Gravity;
                        inSpecialAnimationWithPlayer.deadBody.bodyBleedingHeavily = true;
                        isAgressive = false;

                        DoAnimationClientRpc("pin");
                        SwitchToBehaviourState((int)State.Pin);

                        yield return new WaitForSeconds(3f);
                    }
            }
            else yield return new WaitForSeconds(2f);

            inSpecialAnimation = false;

            StartSearch(transform.position);
            SwitchToBehaviourState((int)State.SearchingForPlayer);
            creatureVoice.mute = false;
            DoAnimationClientRpc("startWalk");
        }

        [ClientRpc]
        public void PuntKickHitClientRpc()
        {
            LogIfDebugBuild("PuntKickHitClientRPC");
            PlayerControllerB playerControllerB = targetPlayer;
            if (playerControllerB != null)
            {
                LogIfDebugBuild("punt kick hit player!");
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.causeOfDeath = CauseOfDeath.Kicking;
                playerControllerB.DamagePlayer(400);
                isAgressive = false;
            }
        }
    }
}