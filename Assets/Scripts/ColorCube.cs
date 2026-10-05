using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

public class ColorCube : NetworkBehaviour
{
    private Color[] COLORS = { Color.red, Color.green, Color.blue, Color.yellow };

    public NetworkVariable<int> TipoObjeto = new(-1);

    public Vector3 HandSlot = new Vector3(0.6f, 0f, 0.65f);
    public float GrabDistance = 2.5f;

    private Vector3 spawnPoint;

    public override void OnNetworkSpawn()
    {
        print(NetworkObjectId);

        if (TipoObjeto.Value < 0) TipoObjeto.Value = (int)(NetworkObjectId % (ulong)COLORS.Length);
        ActualizarColor(TipoObjeto.Value);
        TipoObjeto.OnValueChanged += AlCambiarTipo;
        spawnPoint = transform.position;
    }

    public override void OnNetworkDespawn()
    {
        TipoObjeto.OnValueChanged -= AlCambiarTipo;
    }

    private void AlCambiarTipo(int anterior, int nuevo) => ActualizarColor(nuevo);

    private void ActualizarColor(int tipo)
    {
        if (tipo < 0 || tipo >= COLORS.Length) return;
        GetComponent<Renderer>().material.color = COLORS[tipo];
    }

    public void Grab(NetworkObject user)
    {
        GetComponent<NetworkRigidbody>().SetIsKinematic(true);
        NetworkObject.TrySetParent(user, false);
        transform.localPosition = HandSlot;
    }
    public void ThrowObj(Vector3 throwSpeed)
    {
        NetworkObject.TryRemoveParent(true);

        GetComponent<NetworkRigidbody>().SetIsKinematic(false);
        GetComponent<NetworkRigidbody>().SetLinearVelocity(throwSpeed);
    }


    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void GrabRpc(ulong playerNetworkObjectId, RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkObjectId, out NetworkObject player)) return;

        if (transform.parent != null) return;// Ya esta agarrado por alguien

        if (player.OwnerClientId != rpcParams.Receive.SenderClientId) return;

        if (Vector3.Distance(transform.position, player.transform.position) > GrabDistance) return;

        Grab(player);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void DropRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        if (transform.parent == null) return;// No esta agarrado

        NetworkObject holder = transform.parent.GetComponent<NetworkObject>();
        if (holder == null || holder.OwnerClientId != rpcParams.Receive.SenderClientId) return;

        ThrowObj(Vector3.zero);
    }

}
