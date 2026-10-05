using UnityEngine;
using Unity.Netcode;
public class Chest : NetworkBehaviour
{
    public NetworkObject cubePrefab;
    public float TimeToOpenChest;


    public NetworkVariable<ulong> UserOpening = new NetworkVariable<ulong>(ulong.MaxValue);
    public NetworkVariable<float> Progress = new(0f);


    public override void OnNetworkSpawn()
    {
        //OpenChest();
    }

    private void Update()
    {
        if (!IsServer || UserOpening.Value == ulong.MaxValue) return;

        Progress.Value += Time.deltaTime / TimeToOpenChest;
        if (Progress.Value >= 1f) OpenChest();
    }


    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void StartOpeningChestRpc(RpcParams rpcParams = default)
    {
        ulong quien = rpcParams.Receive.SenderClientId;
        if (UserOpening.Value != ulong.MaxValue && UserOpening.Value != quien) return;// El cofre esta ocupado

        UserOpening.Value = quien;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void CancelOpeningChestRpc(RpcParams rpcParams = default)
    {
        ulong quien = rpcParams.Receive.SenderClientId;
        if (UserOpening.Value != quien) return;// El cofre que intentas desactivar no es tuyo

        UserOpening.Value = ulong.MaxValue;
        Progress.Value = 0;
    }
    public void OpenChest()
    {
        for (int i = 0; i < 5; i++)
        {
            Vector3 spawnPoint = transform.position + Quaternion.Euler(0f, i * 90f, 0) * new Vector3(2f, 1f, 0f);

            NetworkObject colorCube = Instantiate(cubePrefab, spawnPoint, Random.rotation);

            colorCube.Spawn();
        }
        NetworkObject.Despawn();
    }
}
