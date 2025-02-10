using System.Collections;
using System.Diagnostics;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace CustomEnnemies
{

    class MagicSpell : NetworkBehaviour
    {
        public float timer;
        public float speed;
        public bool launched;
        public bool launchedByPlayer;
        public bool hit;

        public AudioClip hitSfx;
        public AudioClip launchSfx;
        public AudioClip enemyHitSfx;
        public AudioSource audioSource;

        public Transform turnCompass;
        public GameObject hitFx;
        public GameObject fx;
        public GameObject playerFx;

        [HideInInspector]
        public EnemyAI enemyTarget;
        [HideInInspector]
        public Vector3 serverPosition;
        [HideInInspector]
        public Quaternion serverRotation;
        [HideInInspector]
        public float updateDestinationInterval;
        [HideInInspector]
        public NetworkObject thisNetworkObject;

        public float updatePositionThreshold = 0.4f;

        private PlayerControllerB target;

        void Update()
        {
            if (launched)
            {
                if (timer > 0)
                {
                    timer -= Time.deltaTime;
                    if (!hit)
                    {
                        transform.Translate(Vector3.forward * Time.deltaTime * speed);
                        if (target != null)
                        {
                            turnCompass.LookAt(target.lowerSpine.transform.position);
                            transform.rotation = Quaternion.Lerp(transform.rotation, turnCompass.rotation, 1.2f * Time.deltaTime);
                        }
                    }
                    return;
                }

                if (base.IsServer)
                    Destroy(gameObject);
            }
        }

        public void Launch(int playerTargetId)
        {
            UnityEngine.Debug.Log("launch magic spell from client : " + (int)OwnerClientId);
            LaunchServerRpc(playerTargetId);
        }

        [ServerRpc]
        public void LaunchServerRpc(int playerTargetId)
        {
            LaunchClientRpc(playerTargetId);
        }

        [ClientRpc]
        public void LaunchClientRpc(int playerTargetId)
        {
            audioSource.PlayOneShot(launchSfx);
            fx.SetActive(true);
            target = StartOfRound.Instance.allPlayerScripts[playerTargetId];
            speed = 8;
            launched = true;

            UnityEngine.Debug.Log("Wizard launch spell towards : " + target.playerUsername);
        }

        public void PlayerLaunch()
        {
            UnityEngine.Debug.Log("launch player magic spell from client : " + (int)OwnerClientId);

            PlayerLaunchServerRpc();
        }

        [ServerRpc]
        public void PlayerLaunchServerRpc()
        {
            PlayerLaunchClientRpc();
        }

        [ClientRpc]
        public void PlayerLaunchClientRpc()
        {
            UnityEngine.Debug.Log("player launch fireball client");
            audioSource.PlayOneShot(launchSfx);
            fx.SetActive(true);
            speed = 12;
            launched = true;
            launchedByPlayer = true;
        }

        void OnTriggerEnter(Collider collision)
        {
            if (launched && !hit && base.IsOwner)
            {
                PlayerControllerB playerB = collision.gameObject.GetComponent<PlayerControllerB>();
                if (playerB != null && !playerB.inSpecialInteractAnimation)
                {
                    UnityEngine.Debug.Log("local collide with player : " + playerB.playerUsername);
                    KillPlayerServerRpc((int)playerB.actualClientId);
                    return;
                }

                EnemyAI enemy = collision.gameObject.GetComponentInChildren<EnemyAI>();

                if (enemy == null && collision.transform.parent != null)
                {
                    enemy = collision.transform.parent.GetComponentInParent<EnemyAI>();
                }


                if (enemy != null && !enemy.isEnemyDead)
                {
                    if (!launchedByPlayer && (enemy.GetComponent<MallWizardAI>() || enemy.GetComponent<CBTWizardAI>()))
                    {
                        return;
                    }


                    UnityEngine.Debug.Log("local collide with enemy : " + enemy.name);
                    enemyTarget = enemy;
                    KillEnemyServerRpc();
                }
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (launched && !hit && base.IsOwner)
            {
                if (collision.collider.tag != "Enemy")
                {
                    UnityEngine.Debug.Log("local destroy projectile on collide");
                    DestroyProjectileServerRpc();
                }
            }
        }


        [ServerRpc(RequireOwnership = false)]
        public void DestroyProjectileServerRpc()
        {
            DestroyProjectileClientRpc();
        }

        [ClientRpc]
        public void DestroyProjectileClientRpc()
        {
            UnityEngine.Debug.Log("destroy projectile client");

            hit = true;
            fx.SetActive(false);
            hitFx.SetActive(true);
        }

        [ServerRpc(RequireOwnership = false)]
        public void KillEnemyServerRpc()
        {
            KillEnemyClientRpc();
        }

        [ClientRpc]
        public void KillEnemyClientRpc()
        {
            hit = true;
            fx.SetActive(false);
            hitFx.SetActive(true);

            audioSource.PlayOneShot(enemyHitSfx);

            if (enemyTarget != null)
            {
                enemyTarget.SetEnemyStunned(true, 2);

                UnityEngine.Debug.Log("kill Enemy on client : " + enemyTarget.name);

                enemyTarget.KillEnemyOnOwnerClient();
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void KillPlayerServerRpc(int playerObjectId)
        {
            KillPlayerClientRpc(playerObjectId);
        }

        [ClientRpc]
        public void KillPlayerClientRpc(int playerObjectId)
        {
            hit = true;
            launched = false;

            fx.SetActive(false);
            hitFx.SetActive(true);
            playerFx.SetActive(true);

            audioSource.PlayOneShot(hitSfx);

            PlayerControllerB playerB = StartOfRound.Instance.allPlayerScripts[playerObjectId];
            playerB.SyncBodyPositionWithClients();
            playerB.inSpecialInteractAnimation = true;
            playerB.snapToServerPosition = true;

            transform.position = playerB.transform.position + new Vector3(0, 1.4f, 0);
            transform.rotation = Quaternion.identity;

            UnityEngine.Debug.Log("kill Player on client : " + playerB.name);

            StartCoroutine(KillPlayer(playerB));
        }

        IEnumerator KillPlayer(PlayerControllerB playerB)
        {
            yield return new WaitForSeconds(2.8f);
            playerB.KillPlayer(Vector3.zero, true, CauseOfDeath.Crushing);

            playerB.inSpecialInteractAnimation = false;
            playerFx.SetActive(false);

            yield return new WaitForSeconds(1f);

            if (base.IsServer)
                Destroy(gameObject);
        }
    }
}
