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
        public AudioSource stepSound = null!;
#pragma warning restore 0649
        float timeSinceHittingLocalPlayer;
        float timeSinceNewRandPos;
        Vector3 positionRandomness;
        Vector3 StalkPos;
        Vector3 spawnPos;
        System.Random enemyRandom = null!;
        bool isDeadAnimationDone;
        bool muteVoice;
        bool muteSteps;
        bool isInterrupted;
        float fleaTimer;
        Transform playerParent;
        PlayerControllerB attackedPlayer;
        FlowermanAI flowermanTarget = null!;
        enum State {
            SearchingForPlayer,
            ChasePlayer,
            ChaseBracken,
            AttackPlayer,
            AttackBracken,
            Flea,
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start() {
            base.Start();
            LogIfDebugBuild("Broly Spawned");
            timeSinceHittingLocalPlayer = 0;
            muteVoice = true;
            timeSinceNewRandPos = 0;
            positionRandomness = new Vector3(0, 0, 0);
            agent.acceleration = 15f;
            spawnPos = transform.position;
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

            if(creatureVoice.mute != muteVoice)
                creatureVoice.mute = muteVoice;

            if (stepSound.mute != muteSteps)
                stepSound.mute = muteSteps;

            timeSinceHittingLocalPlayer += Time.deltaTime;
            timeSinceNewRandPos += Time.deltaTime;

            var state = currentBehaviourStateIndex;
            if (targetPlayer != null && (state == (int)State.ChasePlayer || state == (int)State.ChaseBracken)) {
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
            }
            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
                syncMovementSpeed = 0f;
            }

            if (fleaTimer > 0f)
            {
                fleaTimer -= Time.deltaTime;
            }
        }

        public override void DoAIInterval() {

            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            };

            switch (currentBehaviourStateIndex) {
                case (int)State.SearchingForPlayer:
                    agent.angularSpeed = 250;
                    muteVoice = true;
                    muteSteps = false;
                    if (FoundBrackenInMap() && flowermanTarget != null)
                    {
                        LogIfDebugBuild("found bracken");
                        SwitchToBehaviourClientRpc((int)State.ChaseBracken);
                    }
                    else if (FoundClosestPlayerInRange(4f) && targetPlayer != null) {
                        SwitchToBehaviourClientRpc((int)State.ChasePlayer);
                    }

                    break;
                case (int)State.ChasePlayer:
                    agent.speed = 8f;
                    syncMovementSpeed = 8f;
                    agent.angularSpeed = 200;
                    muteVoice = false;
                    muteSteps = true;
                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (!TargetClosestPlayerInAnyCase() || FoundBrackenInMap() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 15 && !CheckLineOfSightForPosition(targetPlayer.transform.position))) {
                        LogIfDebugBuild("Stop Target Player");
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        return;
                    }

                    ChasePlayer();
                    break;
                case (int)State.ChaseBracken:
                    agent.speed = 15f;
                    syncMovementSpeed = 15f;
                    agent.angularSpeed = 250;
                    muteVoice = true;
                    muteSteps = false;
                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (flowermanTarget == null)
                    {
                        LogIfDebugBuild("Stop Target Bracken");
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        creatureAnimator.SetTrigger("startWalk");
                        return;
                    }

                    ChaseBracken();
                    break;

                case (int)State.AttackPlayer:
                case (int)State.AttackBracken:
                    agent.angularSpeed = 250;
                    agent.speed = 0f;
                    syncMovementSpeed = 0f;
                    muteVoice = true;
                    muteSteps = true;
                    // We don't care about doing anything here
                    break;

                case (int)State.Flea:
                    agent.speed = 15f;
                    syncMovementSpeed = 15f;
                    agent.angularSpeed = 250;
                    muteVoice = true;
                    muteSteps = false;
                    if (attackedPlayer != null && attackedPlayer.deadBody != null)
                    {
                        attackedPlayer.deadBody.SetRagdollPositionSafely(assParent.position);
                    }

                    if (fleaTimer <= 0 || (Vector3.Distance(transform.position, spawnPos) < 1))
                    {
                        if(attackedPlayer != null)
                        {
                            DropPlayerClientRpc();
                            attackedPlayer = null;
                        }

                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
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
            if (FindObjectsOfType<FlowermanAI>().Length > 0)
            {
                for(int i = 0; i < FindObjectsOfType<FlowermanAI>().Length; i++)
                {
                    if (FindObjectsOfType<FlowermanAI>()[i].isEnemyDead == false)
                    {
                        flowermanTarget = FindObjectsOfType<FlowermanAI>()[i];
                        break;
                    }
                }

                return flowermanTarget != null;
            }
            else return false;
        }

        bool FoundClosestPlayerInRange(float range) {
            mostOptimalDistance = range;
            targetPlayer = null;

            PlayerControllerB[] playersInSight = GetAllPlayersInLineOfSight(180, 60, eye);
            if (playersInSight != null)
            {
                for (int i = 0; i < playersInSight.Length; i++)
                {
                    targetPlayer = playersInSight[i];
                    break;
                }
            }

            agent.speed = 2.5f;
            syncMovementSpeed = 2.5f;
            agent.angularSpeed = 200;

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

        void ChasePlayer() {
            // We only run this method for the host because I'm paranoid about randomness not syncing I guess
            // This is fine because the game does sync the position of the enemy.
            // Also the attack is a ClientRpc so it should always sync
            if (targetPlayer == null || !IsOwner) {
                return;
            }

            StalkPos = targetPlayer.transform.position;
            //SetDestinationToPosition(StalkPos, checkForPath: false);
            SetMovingTowardsTargetPlayer(targetPlayer);

            if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 1.2f){
                StartCoroutine(AttackPlayer());
            }
        }

        void ChaseBracken()
        {
            // We only run this method for the host because I'm paranoid about randomness not syncing I guess
            // This is fine because the game does sync the position of the enemy.
            // Also the attack is a ClientRpc so it should always sync
            if (!IsOwner)
            {
                return;
            }

            StalkPos = flowermanTarget.transform.position;
            SetDestinationToPosition(StalkPos, checkForPath: false);

            if (Vector3.Distance(transform.position, flowermanTarget.transform.position) < 2f)
            {
                StartCoroutine(AttackBracken());
            }
        }

        void FleaPlayer()
        {
            LogIfDebugBuild("Flea player");
            SwitchToBehaviourClientRpc((int)State.Flea);
            SetDestinationToPosition(spawnPos);

            fleaTimer = 8f;
        }

        IEnumerator AttackPlayer() {

            attackedPlayer = null;
            SwitchToBehaviourClientRpc((int)State.AttackPlayer);
            transform.position = targetPlayer.transform.position;
            agent.velocity = Vector3.zero;

            if(isEnemyDead){
                yield break;
            }
            StopPlayerClientRpc();

            isInterrupted = false;
            int hitCount = 0;

            attackedPlayer = targetPlayer;
            LogIfDebugBuild("attacking : " + targetPlayer.name);

            while (isInterrupted == false)
            {
                hitCount++;
                LogIfDebugBuild("PlayerHealth : " + targetPlayer.health + " hit count : " + hitCount);
                PlayerHitClientRpc();

                if (hitCount == 12 || targetPlayer.isPlayerDead || isInterrupted == true)
                    break;

                yield return new WaitForSeconds(0.5f);

                yield return null;
            }

            LogIfDebugBuild("Release player");

            ReleasePlayerClientRpc();

            if (isInterrupted == false)
            {
                targetPlayer.causeOfDeath = CauseOfDeath.Suffocation;
                DragPlayerClientRpc();
            }

            SwitchToBehaviourClientRpc((int)State.Flea);
            FleaPlayer();
        }

        IEnumerator AttackBracken()
        {
            LogIfDebugBuild("Attack Bracken");
            SwitchToBehaviourClientRpc((int)State.AttackBracken);
            flowermanTarget.KillEnemyOnOwnerClient();

            yield return new WaitForSeconds(3f);

            StartSearch(transform.position);
            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
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
                playerControllerB.DamagePlayer(35);
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1) {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if(isEnemyDead){
                return;
            }

            if (!isInterrupted)
            {
                LogIfDebugBuild("Interrupt enemy");
                isInterrupted = true;

                if(attackedPlayer!= null)
                {
                    attackedPlayer = null;
                }
            }

            enemyHP -= force;
            if (IsOwner) {
              
              
                if (enemyHP <= 0 && !isEnemyDead) {
                    // Our death sound will be played through creatureVoice when KillEnemy() is called.
                    // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                    // so we don't need to call a death animation ourselves.
                    StopCoroutine(AttackPlayer());
                    // We need to stop our search coroutine, because the game does not do that by default.
                    StopCoroutine(searchCoroutine);

                    creatureVoice.mute = false;
                    stepSound.mute = true;
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
        public void PlayerHitClientRpc()
        {
            LogIfDebugBuild("HitClientRPC");
            PlayerControllerB playerControllerB = targetPlayer;
            if (playerControllerB != null)
            {
                LogIfDebugBuild("hit player!");
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.DamagePlayer(10);
            }
        }

        [ClientRpc]
        public void StopPlayerClientRpc()
        {
            LogIfDebugBuild("StopPlayerClientRpc");
            PlayerControllerB playerControllerB = targetPlayer;
            if (playerControllerB != null)
            {
                playerControllerB.disableLookInput = true;
                playerControllerB.disableMoveInput = true;
                playerControllerB.voiceMuffledByEnemy = true;
                playerControllerB.DropAllHeldItemsClientRpc();
                playerControllerB.disableInteract = true;
            }
        }

        [ClientRpc]
        public void ReleasePlayerClientRpc()
        {
            LogIfDebugBuild("ReleasePlayerClientRpc");
            PlayerControllerB playerControllerB = targetPlayer;
            if (playerControllerB != null)
            {
                playerControllerB.disableLookInput = false;
                playerControllerB.disableInteract = false;
                playerControllerB.disableMoveInput = false;
                playerControllerB.voiceMuffledByEnemy = false;
                playerControllerB.redirectToEnemy = null;
            }
        }

        [ClientRpc]
        public void DragPlayerClientRpc()
        {
            LogIfDebugBuild("DragPlayerClientRpc");
            PlayerControllerB playerControllerB = attackedPlayer;

            if(!playerControllerB.isPlayerDead)
                playerControllerB.SpawnDeadBody(playerControllerB.GetInstanceID(), Vector3.zero, (int)CauseOfDeath.Suffocation, playerControllerB);

            if (playerControllerB.deadBody != null)
            {
                playerParent = playerControllerB.deadBody.transform.parent;

                playerControllerB.deadBody.speedMultiplier = 0;
                playerControllerB.deadBody.physicsParent = assParent;
                playerControllerB.deadBody.maxVelocity = 0;
                playerControllerB.deadBody.transform.SetParent(assParent);
                playerControllerB.deadBody.canBeGrabbedBackByPlayers = false;
            }
        }

        [ClientRpc]
        public void DropPlayerClientRpc()
        {
            PlayerControllerB playerControllerB = attackedPlayer;
            if(playerControllerB.deadBody!= null)
            {
                playerControllerB.deadBody.transform.SetParent(playerParent);
                playerControllerB.deadBody.canBeGrabbedBackByPlayers = true;
                playerControllerB.playerRigidbody.mass = 1;
            }      
        }
    }
}

