using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public struct RanuraInventario : INetworkSerializable, IEquatable<RanuraInventario>
{
    public int tipo;
    public int cantidad;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref tipo);
        serializer.SerializeValue(ref cantidad);
    }

    public bool Equals(RanuraInventario other) => tipo == other.tipo && cantidad == other.cantidad;
}

public class PlayerAvatar : NetworkBehaviour
{
    public InputSystem_Actions inputs;
    public float Speed;
    public float GrabDistance = 3f;
    public float ChestDistance = 3f;
    public float SpawnSeparation = 3f;
    public NetworkObject cubePrefab;
    public int MaxInventorySlots = 9;

    public NetworkList<RanuraInventario> inventario;
    private Vector2 moveInput;
    private string inventoryMessage = "";

    private void Awake()
    {
        inputs = new InputSystem_Actions();
        inputs.Enable();
        inventario = new NetworkList<RanuraInventario>();
    }

    private void OnEnable()
    {
        if (inputs == null) return;
        inputs.Enable();
        inputs.Player.Move.performed += OnMove;
        inputs.Player.Move.canceled += OnMoveCanceled;
        inputs.Player.Interact.performed += OnInteract;
        inputs.Player.Drop.performed += OnDrop;
    }

    private void OnDisable()
    {
        if (inputs == null) return;
        inputs.Player.Move.performed -= OnMove;
        inputs.Player.Move.canceled -= OnMoveCanceled;
        inputs.Player.Interact.performed -= OnInteract;
        inputs.Player.Drop.performed -= OnDrop;
        inputs.Disable();
    }

    private void OnMove(InputAction.CallbackContext ctx) => moveInput = ctx.ReadValue<Vector2>();
    private void OnMoveCanceled(InputAction.CallbackContext ctx) => moveInput = Vector2.zero;

    private void OnInteract(InputAction.CallbackContext ctx)
    {
        if (!IsOwner) return;

        ColorCube cube = NearestCube();
        if (cube != null && Vector3.Distance(transform.position, cube.transform.position) <= GrabDistance)
        {
            RecogerCuboRpc(cube.NetworkObjectId);
            return;
        }

        Chest nearChest = NearestChest();
        if (nearChest != null && Vector3.Distance(transform.position, nearChest.transform.position) <= ChestDistance)
            InteractWithChestRpc(nearChest.NetworkObjectId);
    }

    private void OnDrop(InputAction.CallbackContext ctx)
    {
        if (!IsOwner || inventario == null || inventario.Count == 0) return;
        SoltarObjetoRpc(0);
    }

    private ColorCube NearestCube()
    {
        ColorCube nearest = null;
        float minDistance = float.MaxValue;
        foreach (ColorCube cube in FindObjectsByType<ColorCube>(FindObjectsSortMode.None))
        {
            if (cube.transform.parent != null) continue;
            float distance = Vector3.Distance(transform.position, cube.transform.position);
            if (distance < minDistance) { minDistance = distance; nearest = cube; }
        }
        return nearest;
    }

    private Chest NearestChest()
    {
        Chest nearest = null;
        float minDistance = float.MaxValue;
        foreach (Chest chest in FindObjectsByType<Chest>(FindObjectsSortMode.None))
        {
            float distance = Vector3.Distance(transform.position, chest.transform.position);
            if (distance < minDistance) { minDistance = distance; nearest = chest; }
        }
        return nearest;
    }

    public override void OnNetworkSpawn()
    {
        inventario.OnListChanged += AlCambiarInventario;
        if (IsOwner)
        {
            transform.position += new Vector3((float)OwnerClientId * SpawnSeparation, 0f, 0f);
            Debug.Log("[Inventario] listo: " + inventario.Count + " tipos de objetos");
        }
    }

    public override void OnNetworkDespawn()
    {
        inventario.OnListChanged -= AlCambiarInventario;
    }

    private void AlCambiarInventario(NetworkListEvent<RanuraInventario> e)
    {
        inventoryMessage = $"{e.Type} en la ranura {e.Index}: tipo {e.Value.tipo}, cantidad {e.Value.cantidad}";
        Debug.Log("[Inventario] " + inventoryMessage);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RecogerCuboRpc(ulong cubeNetworkObjectId, RpcParams rpcParams = default)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(cubeNetworkObjectId, out NetworkObject cubeObject)) return;
        if (!cubeObject.TryGetComponent(out ColorCube cube)) return;
        if (Vector3.Distance(transform.position, cube.transform.position) > GrabDistance) return;

        if (!Guardar(cube.TipoObjeto.Value)) return;
        cubeObject.Despawn(true);
    }

    private bool Guardar(int tipo)
    {
        for (int i = 0; i < inventario.Count; i++)
        {
            if (inventario[i].tipo != tipo) continue;
            RanuraInventario ranura = inventario[i];
            ranura.cantidad++;
            inventario[i] = ranura;
            return true;
        }

        if (inventario.Count >= MaxInventorySlots)
        {
            Debug.Log("[Inventario] Lleno; no se recogió el cubo.");
            return false;
        }

        inventario.Add(new RanuraInventario { tipo = tipo, cantidad = 1 });
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SoltarObjetoRpc(int slot, RpcParams rpcParams = default)
    {
        if (slot < 0 || slot >= inventario.Count || cubePrefab == null) return;

        RanuraInventario ranura = inventario[slot];
        Vector3 position = transform.position + transform.forward * 1.5f + Vector3.up * 0.5f;
        NetworkObject dropped = Instantiate(cubePrefab, position, Quaternion.identity);
        if (dropped.TryGetComponent(out ColorCube cube)) cube.TipoObjeto.Value = ranura.tipo;
        dropped.Spawn();

        ranura.cantidad--;
        if (ranura.cantidad <= 0) inventario.RemoveAt(slot);
        else inventario[slot] = ranura;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void InteractWithChestRpc(ulong chestNetworkObjectId, RpcParams rpcParams = default)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(chestNetworkObjectId, out NetworkObject chestObj)) return;
        if (!chestObj.TryGetComponent(out Chest chest)) return;
        if (Vector3.Distance(transform.position, chest.transform.position) > ChestDistance) return;

        ulong quien = rpcParams.Receive.SenderClientId;
        if (chest.UserOpening.Value == quien) chest.CancelOpeningChestRpc();
        else chest.StartOpeningChestRpc();
    }

    private void Update()
    {
        if (!IsOwner) return;
        Vector3 dir = new Vector3(moveInput.x, 0, moveInput.y);
        transform.position += dir * Speed * Time.deltaTime;
    }

    private void OnGUI()
    {
        if (!IsOwner || !IsSpawned) return;
        GUILayout.BeginArea(new Rect(12, 12, 300, 250), GUI.skin.box);
        GUILayout.Label("INVENTARIO (E recoger / Q soltar 1)");
        if (inventario != null)
        {
            for (int i = 0; i < inventario.Count; i++)
                GUILayout.Label($"Ranura {i + 1}: Cubo tipo {inventario[i].tipo} x{inventario[i].cantidad}");
        }
        if (!string.IsNullOrEmpty(inventoryMessage)) GUILayout.Label(inventoryMessage);
        GUILayout.EndArea();
    }
}
