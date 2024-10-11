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
        bool attackingPlayer;
        bool isInterrupted;
        float fleaTimer;
        float baseJumpForce;
        int hitCount;

        private Ray playerRay;

        public bool carryingPlayerBody;

        public DeadBodyInfo bodyBeingCarried;

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
            timeSinceNewRandPos = 0;
            creatureAnimator.SetTrigger("startWalk");
            positionRandomness = new Vector3(0, 0, 0);
            agent.acceleration = 15f;
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

        public override void Update() {
            base.Update();
            if (isEnemyDead) {
                // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
                if (!isDeadAnimationDone) {
                    LogIfDebugBuild("Stopping enemy voice with janky code.");
                    isDeadAnimationDone = true;
                    MuteVoiceClientRpc(true);
                    MuteStepsClientRpc(true);
                    PlayDeathSoundClientRpc();
                }
                return;
            }

            timeSinceHittingLocalPlayer += Time.deltaTime;
            timeSinceNewRandPos += Time.deltaTime;

            var state = currentBehaviourStateIndex;
            if (targetPlayer != null && state == (int)State.ChasePlayer) 
            {
                SetDestinationToPosition(targetPlayer.transform.position);
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
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

        public override void DoAIInterval() {

            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            };

            switch (currentBehaviourStateIndex) {
                case (int)State.SearchingForPlayer:
                    agent.speed = 2.5f;
                    if (FoundBrackenInMap() && flowermanTarget != null)
                    {
                        LogIfDebugBuild("found bracken");
                        creatureAnimator.SetTrigger("startWalk");
                        SwitchToBehaviourClientRpc((int)State.ChaseBracken);
                        MuteStepsClientRpc(true);
                        MuteVoiceClientRpc(true);
                    }
                    else if (FoundClosestPlayerInRange(20f, 3f) && targetPlayer != null) {

                        creatureAnimator.SetTrigger("chase");
                        SwitchToBehaviourClientRpc((int)State.ChasePlayer);
                        MuteStepsClientRpc(true);
                        MuteVoiceClientRpc(false);
                    }

                    break;
                case (int)State.ChasePlayer:
                    agent.speed = 6.5f;
                    agent.angularSpeed = 500f;
                    agent.acceleration = 20f;
                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (!TargetClosestPlayerInAnyCase() || FoundBrackenInMap() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 15 && !CheckLineOfSightForPosition(targetPlayer.transform.position))) {

                        if (targetPlayer != GameNetworkManager.Instance.localPlayerController)
                        {
                            ChangeOwnershipOfEnemy(targetPlayer.actualClientId);
                        }                     

                        LogIfDebugBuild("Stop Target Player");
                        StartSearch(transform.position);
                        creatureAnimator.SetTrigger("startWalk");
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        MuteStepsClientRpc(false);
                        MuteVoiceClientRpc(true);
                        return;
                    }

                    ChasePlayer();
                    break;
                case (int)State.ChaseBracken:
                    agent.speed = 15f;
                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (flowermanTarget == null)
                    {
                        LogIfDebugBuild("Stop Target Bracken");
                        StartSearch(transform.position);
                        creatureAnimator.SetTrigger("startWalk");
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        MuteVoiceClientRpc(true);
                        MuteStepsClientRpc(false);
                        return;
                    }

                    ChaseBracken();
                    break;

                case (int)State.AttackPlayer:
                    agent.speed = 0;
                    hitCount++;
                    targetPlayer.DamagePlayer(5);

                    if (hitCount == 21 || targetPlayer.isPlayerDead || isInterrupted == true)
                    {
                        LogIfDebugBuild("Release player");
                        //ReleasePlayerClientRpc();

                        inSpecialAnimationWithPlayer.inSpecialInteractAnimation = false;
                        inSpecialAnimationWithPlayer.snapToServerPosition = false;
                        inSpecialAnimationWithPlayer.voiceMuffledByEnemy = false;

                        if (inSpecialAnimationWithPlayer.deadBody != null)
                        {
                            inSpecialAnimationWithPlayer.causeOfDeath = CauseOfDeath.Suffocation;
                            bodyBeingCarried = inSpecialAnimationWithPlayer.deadBody;
                            bodyBeingCarried.attachedTo = assParent;
                            bodyBeingCarried.attachedLimb = inSpecialAnimationWithPlayer.deadBody.bodyParts[0];
                            bodyBeingCarried.matchPositionExactly = true;
                            carryingPlayerBody = true;
                        }

                        MuteStepsClientRpc(true);
                        MuteVoiceClientRpc(true);
                        FleaPlayer();
                    }
                    break;
                case (int)State.AttackBracken:
                    agent.speed = 0f;
                    // We don't care about doing anything here
                    break;

                case (int)State.Flea:
                    agent.speed = 15f;

                    if (fleaTimer <= 0 || (Vector3.Distance(transform.position, spawnPos) < 1))
                    {
                        if(carryingPlayerBody)
                        {
                            DropPlayerBody();
                        }

                        StartSearch(transform.position);
                        creatureAnimator.SetTrigger("startWalk");
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        MuteVoiceClientRpc(true);
                        MuteStepsClientRpc(true);
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
                for(int i = 0; i < FindObjectsOfType<FlowermanAI>().Length; i++)
                {
                    if (FindObjectsOfType<FlowermanAI>()[i].isEnemyDead == false && Vector3.Distance(transform.position, FindObjectsOfType<FlowermanAI>()[i].transform.position) < mostOptimalDistance)
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

        private void DropPlayerBody()
        {
            if (carryingPlayerBody)
            {
                carryingPlayerBody = false;
                bodyBeingCarried.matchPositionExactly = false;
                bodyBeingCarried.attachedTo = null;
                bodyBeingCarried = null;
            }
        }

        void ChasePlayer() {
            if (targetPlayer == null || !IsOwner)
            {
                return;
            }

            StalkPos = targetPlayer.transform.position;

            if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 1.2f){
               StartCoroutine(AttackPlayer());
            }
        }

        void ChaseBracken()
        {
            StalkPos = flowermanTarget.transform.position;
            SetDestinationToPosition(StalkPos, checkForPath: false);

            if (Vector3.Distance(transform.position, flowermanTarget.transform.position) < 2f)
            {
                StartCoroutine(AttackBracken());
            }
        }

        void FleaPlayer(bool carryingBody = true)
        {
            LogIfDebugBuild("Flea player");
            creatureAnimator.SetTrigger("startWalk");
            SwitchToBehaviourClientRpc((int)State.Flea);
            MuteVoiceClientRpc(true);
            MuteStepsClientRpc(true);
            SetDestinationToPosition(spawnPos);

            fleaTimer = 8f;

            if (inSpecialAnimationWithPlayer != null)
            {
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
        }

        IEnumerator AttackPlayer() {
            LogIfDebugBuild("attacking : " + targetPlayer.playerUsername);

            creatureAnimator.SetTrigger("attack");
            SwitchToBehaviourClientRpc((int)State.AttackPlayer);
            MuteStepsClientRpc(true);
            MuteVoiceClientRpc(true);

            inSpecialAnimationWithPlayer = targetPlayer;
            inSpecialAnimationWithPlayer.inSpecialInteractAnimation = true;
            inSpecialAnimationWithPlayer.snapToServerPosition = true;
            inSpecialAnimationWithPlayer.DropAllHeldItemsClientRpc();
            inSpecialAnimationWithPlayer.voiceMuffledByEnemy = true;

            // StopPlayerClientRpc();

            agent.velocity = Vector3.zero;
            hitCount = 0;

            isInterrupted = false;

            Vector3 startingPosition = base.transform.position;
            for (int i = 0; i < 5; i++)
            {
                base.transform.position = Vector3.Lerp(startingPosition, targetPlayer.transform.position, (float)i / 5f);
                yield return null;
            }
            base.transform.position = targetPlayer.transform.position;
        }

        IEnumerator AttackBracken()
        {
            LogIfDebugBuild("Attack Bracken");
            SwitchToBehaviourClientRpc((int)State.AttackBracken);
            MuteStepsClientRpc(true);
            MuteVoiceClientRpc(true);

            FlowermanAI flowermanToKill = null;
            if (FindObjectsOfType<FlowermanAI>().Length > 0)
            {
                for (int i = 0; i < FindObjectsOfType<FlowermanAI>().Length; i++)
                {
                    if (FindObjectsOfType<FlowermanAI>()[i].isEnemyDead == false && Vector3.Distance(transform.position, FindObjectsOfType<FlowermanAI>()[i].transform.position) < mostOptimalDistance)
                    {
                        flowermanToKill = FindObjectsOfType<FlowermanAI>()[i];
                        mostOptimalDistance = Vector3.Distance(transform.position, FindObjectsOfType<FlowermanAI>()[i].transform.position);
                        break;
                    }
                }

                if(flowermanToKill != null)
                    flowermanToKill.KillEnemyOnOwnerClient();
            }

            yield return new WaitForSeconds(3f);

            StartSearch(transform.position);
            creatureAnimator.SetTrigger("startWalk");
            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            MuteVoiceClientRpc(true);
            MuteStepsClientRpc(false);
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
                //ReleasePlayerClientRpc();
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
            }

            if(currentBehaviourStateIndex == (int)State.SearchingForPlayer && playerWhoHit != null)
            {
                LogIfDebugBuild("Chase " + playerWhoHit + " after being hit");
                targetPlayer = playerWhoHit;
                creatureAnimator.SetTrigger("chase");
                SwitchToBehaviourClientRpc((int)State.ChasePlayer);
                MuteVoiceClientRpc(false);
                MuteStepsClientRpc(true);
            }

            enemyHP -= force;
            if (IsOwner) {
              
              
                if (enemyHP <= 0) {
                    StopCoroutine(searchCoroutine);

                    MuteVoiceClientRpc(false);
                    MuteStepsClientRpc(true);
                    KillEnemyOnOwnerClient();
                }
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
            LogIfDebugBuild("Mute voice : " + mute);
            creatureVoice.mute = mute;
        }

        [ClientRpc]
        public void MuteStepsClientRpc(bool mute)
        {
            LogIfDebugBuild("Mute voice : " + mute);
            stepSound.mute = mute;
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName) {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }
        /*
        [ClientRpc]
        public void PlayerHitClientRpc()
        {
            LogIfDebugBuild("HitClientRPC");
            PlayerControllerB playerControllerB = targetPlayer;
            if (playerControllerB != null)
            {
                LogIfDebugBuild("hit player!");
                playerControllerB.DamagePlayer(5);
            }
        }

        [ClientRpc]
        public void StopPlayerClientRpc()
        {
            PlayerControllerB playerControllerB = targetPlayer;
            if (playerControllerB != null)
            {
                LogIfDebugBuild("StopPlayerClientRpc : " + targetPlayer.playerUsername);

                inSpecialAnimationWithPlayer = playerControllerB;
                inSpecialAnimationWithPlayer.inSpecialInteractAnimation = true;
                inSpecialAnimationWithPlayer.snapToServerPosition = true;

                playerControllerB.DropAllHeldItemsClientRpc();
               // playerControllerB.disableInteract = true;
               // playerControllerB.disableLookInput = true;
               //  playerControllerB.disableMoveInput = true;
                playerControllerB.voiceMuffledByEnemy = true;
               // baseJumpForce = playerControllerB.jumpForce;
               // playerControllerB.jumpForce = 0;
            }
        }

        [ClientRpc]
        public void ReleasePlayerClientRpc()
        {
            PlayerControllerB playerControllerB = targetPlayer;
            if (playerControllerB != null)
            {
                LogIfDebugBuild("ReleasePlayerClientRpc : " + targetPlayer.playerUsername);

                inSpecialAnimationWithPlayer.inSpecialInteractAnimation = false;
                inSpecialAnimationWithPlayer.snapToServerPosition = false;

               // playerControllerB.disableLookInput = false;
               // playerControllerB.disableInteract = false;
               // playerControllerB.disableMoveInput = false;
                playerControllerB.voiceMuffledByEnemy = false;
              //  playerControllerB.redirectToEnemy = null;
              //  playerControllerB.jumpForce = baseJumpForce;
            }
        }

        [ClientRpc]
        public void DragPlayerBodyClientRpc()
        {
            if (carryingPlayerBody)
            {
                carryingPlayerBody = false;
                bodyBeingCarried.matchPositionExactly = false;
                bodyBeingCarried.attachedTo = null;
                bodyBeingCarried = null;
            }
        }

        [ClientRpc]
        public void DropPlayerBodyClientRpc()
        {
            if (carryingPlayerBody)
            {
                carryingPlayerBody = false;
                bodyBeingCarried.matchPositionExactly = false;
                bodyBeingCarried.attachedTo = null;
                bodyBeingCarried = null;
            }
        }
        */
    }
}

