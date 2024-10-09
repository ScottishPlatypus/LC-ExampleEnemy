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
            creatureAnimator.SetTrigger("startWalk");
            timeSinceNewRandPos = 0;
            positionRandomness = new Vector3(0, 0, 0);
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
                    creatureVoice.Stop();
                    creatureVoice.PlayOneShot(dieSFX);
                }
                return;
            }
            timeSinceHittingLocalPlayer += Time.deltaTime;
            timeSinceNewRandPos += Time.deltaTime;

            var state = currentBehaviourStateIndex;
            if (targetPlayer != null && state == (int)State.ChasePlayerRko || state == (int)State.ChasePlayerPuntKick) {
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

        public override void DoAIInterval() {

            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            };

            switch (currentBehaviourStateIndex) {
                case (int)State.SearchingForPlayer:
                    if (FoundClosestPlayerInRange(15f) && targetPlayer != null) {
                        if (targetPlayer.health <= 90 || isAgressive)
                        {
                            LogIfDebugBuild("Start Target Player For PuntKick");
                            StopSearch(currentSearch);
                            creatureVoice.mute = true;

                            SwitchToBehaviourClientRpc((int)State.ChasePlayerPuntKick);
                            creatureAnimator.SetTrigger("puntKickChase");
                            return;
                        }
                        else if (!targetPlayer.HasLineOfSightToPosition(transform.position))
                        {
                            LogIfDebugBuild("Start Target Player For Rko");
                            StopSearch(currentSearch);
                            creatureVoice.mute = true;
                            SwitchToBehaviourClientRpc((int)State.ChasePlayerRko);
                            creatureAnimator.SetTrigger("rkoChase");
                            return;
                        }
                    }
                    break;

                case (int)State.ChasePlayerRko:
                    agent.speed = 10f;
                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (!TargetClosestPlayerInAnyCase() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 25 && !CheckLineOfSightForPosition(targetPlayer.transform.position))) {
                        LogIfDebugBuild("Stop Target Player");
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        creatureVoice.mute = false;
                        creatureAnimator.SetTrigger("startWalk");
                        return;
                    }

                    if (targetPlayer.health <= 90 || isAgressive)
                    {
                        LogIfDebugBuild("Player is down under 50 hp");
                        SwitchToBehaviourClientRpc((int)State.ChasePlayerPuntKick);
                        creatureAnimator.SetTrigger("puntKickChase");
                        return;
                    }
                    else if(targetPlayer.HasLineOfSightToPosition(transform.position) == true)
                    {
                        LogIfDebugBuild("Player target has sight on Randy");
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        creatureVoice.mute = false;
                        creatureAnimator.SetTrigger("startWalk");
                        return;
                    }

                    ChasePlayerRko();
                    break;
                case (int)State.ChasePlayerPuntKick:
                    agent.speed = 15f;
                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (!TargetClosestPlayerInAnyCase() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 25 && !CheckLineOfSightForPosition(targetPlayer.transform.position)) || (targetPlayer.health > 90 && !isAgressive) )
                    {
                        LogIfDebugBuild("Stop Target Player");
                        isAgressive = false;
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        creatureVoice.mute = false;
                        creatureAnimator.SetTrigger("startWalk");
                        return;
                    }
                    ChasePlayerPuntKick();
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

        bool FoundClosestPlayerInRange(float range) {
            mostOptimalDistance = range;
            targetPlayer = null;

            PlayerControllerB[] playersInSight = GetAllPlayersInLineOfSight(45, 60, eye);
            if (playersInSight != null)
            {
                for (int i = 0; i < playersInSight.Length; i++)
                {
                    if (playersInSight[i].HasLineOfSightToPosition(transform.position) == false || playersInSight[i].health <= 90 || isAgressive)
                    {
                        // LogIfDebugBuild("Player n°" + i + " doesnt have sight on Randy");
                        targetPlayer = playersInSight[i];
                        break;
                    }
                }
            }

            agent.speed = 6f;
            for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
            {
                if (StartOfRound.Instance.allPlayerScripts[i].HasLineOfSightToPosition(transform.position))
                {
                    agent.speed = 2.5f;
                }
            }

            /*
            if (targetPlayer == null)
            {
                for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
                {
                    tempDist = Vector3.Distance(transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
                    if (tempDist < mostOptimalDistance && !StartOfRound.Instance.allPlayerScripts[i].HasLineOfSightToPosition(transform.position))
                    {
                        mostOptimalDistance = tempDist;
                        targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
                    }
                }
            }
            */
            return targetPlayer != null;
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

        void ChasePlayerRko() {
            // We only run this method for the host because I'm paranoid about randomness not syncing I guess
            // This is fine because the game does sync the position of the enemy.
            // Also the attack is a ClientRpc so it should always sync
            if (targetPlayer == null || !IsOwner) {
                return;
            }

            StalkPos = targetPlayer.transform.position;
            SetDestinationToPosition(StalkPos, checkForPath: false);
           
            if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 3.5f){
                StartCoroutine(RkoAttack());
            }
        }

        void ChasePlayerPuntKick()
        {
            LogIfDebugBuild("TEST V1");
            // We only run this method for the host because I'm paranoid about randomness not syncing I guess
            // This is fine because the game does sync the position of the enemy.
            // Also the attack is a ClientRpc so it should always sync
            if (targetPlayer == null || !IsOwner)
            {
                return;
            }

            StalkPos = targetPlayer.transform.position;
            SetDestinationToPosition(StalkPos, checkForPath: false);

            if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 4.5f)
            {
                StartCoroutine(PuntKick());
            }
        }

        IEnumerator RkoAttack() {
            SwitchToBehaviourClientRpc((int)State.RkoInProgress);
            StalkPos = targetPlayer.transform.position;
            SetDestinationToPosition(StalkPos);
            if(isEnemyDead){
                yield break;
            }
            DoAnimationClientRpc("rko");
            StopPlayerClientRpc();
            yield return new WaitForSeconds(0.5f);
            RkoAttackHitClientRpc();
            yield return new WaitForSeconds(1.2f);

            DoAnimationClientRpc("pin");
            SwitchToBehaviourClientRpc((int)State.Pin);

            yield return new WaitForSeconds(3f);

           // DoAnimationClientRpc("pose");
           // SwitchToBehaviourClientRpc((int)State.Pose);

           // yield return new WaitForSeconds(3f);
            /*
            // In case the player has already gone away, we just yield break (basically same as return, but for IEnumerator)
            if (currentBehaviourStateIndex != (int)State.RkoInProgress){
                yield break;
            }
            */
            StartSearch(transform.position);
            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            creatureVoice.mute = false;
            creatureAnimator.SetTrigger("startWalk");
        }

        IEnumerator PuntKick()
        {
            if (puntKickTimer <= 0f)
            {
                SwitchToBehaviourClientRpc((int)State.PuntKickInProgress);
                puntKickTimer = 7f;
            }
            else SwitchToBehaviourClientRpc((int)State.PuntKickNoSound);

            StalkPos = targetPlayer.transform.position;
            SetDestinationToPosition(StalkPos);
            if (isEnemyDead)
            {
                yield break;
            }
            DoAnimationClientRpc("puntKick");
            yield return new WaitForSeconds(0.2f);
            if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 3f)
            {
                PuntKickHitClientRpc();

                yield return new WaitForSeconds(1.2f);

                DoAnimationClientRpc("pin");
                SwitchToBehaviourClientRpc((int)State.Pin);

                 yield return new WaitForSeconds(3f);

               // DoAnimationClientRpc("pose");
               // SwitchToBehaviourClientRpc((int)State.Pose);

               // yield return new WaitForSeconds(2f);
            }
            else yield return new WaitForSeconds(2f);

            /*
            // In case the player has already gone away, we just yield break (basically same as return, but for IEnumerator)
            if (currentBehaviourStateIndex != (int)State.PuntKickInProgress)
            {
                yield break;
            }
             */
            StartSearch(transform.position);
            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            creatureVoice.mute = false;
            creatureAnimator.SetTrigger("startWalk");
        }

        public override void OnCollideWithPlayer(Collider other) {
            if (timeSinceHittingLocalPlayer < 1f) {
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null && playerControllerB != targetPlayer)
            {
                LogIfDebugBuild("Example Enemy Collision with Player!");
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.DamagePlayer(100);
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
                    SwitchToBehaviourClientRpc((int)State.ChasePlayerPuntKick);
                    creatureAnimator.SetTrigger("puntKickChase");
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
        public void DoAnimationClientRpc(string animationName) {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        public void RkoAttackHitClientRpc() {
            LogIfDebugBuild("RkoAttackHitClientRPC");
            PlayerControllerB playerControllerB = targetPlayer;
            if (playerControllerB != null)
            {
                LogIfDebugBuild("Rko hit player!");
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.causeOfDeath = CauseOfDeath.Strangulation;
                playerControllerB.DamagePlayer(400);

                playerControllerB.disableMoveInput = false;
                playerControllerB.voiceMuffledByEnemy = false;
                playerControllerB.disableLookInput = false;
                playerControllerB.redirectToEnemy = null;
            }
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

        [ClientRpc]
        public void StopPlayerClientRpc()
        {
            LogIfDebugBuild("StopPlayerClientRpc");
            PlayerControllerB playerControllerB = targetPlayer;
            if (playerControllerB != null)
            {
                Vector3 relativePos = transform.position - playerControllerB.transform.position;
                Quaternion rotation = Quaternion.LookRotation(relativePos, Vector3.up);

                playerControllerB.redirectToEnemy = this;
                playerControllerB.syncFullCameraRotation = rotation.eulerAngles;
                playerControllerB.ForceTurnTowardsTarget();
                playerControllerB.disableMoveInput = true;
                playerControllerB.voiceMuffledByEnemy = true;
                playerControllerB.disableLookInput = true;
            }
        }
    }
}