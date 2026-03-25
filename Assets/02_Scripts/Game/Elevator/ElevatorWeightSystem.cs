using System.Runtime.InteropServices.WindowsRuntime;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class ElevatorWeightSystem : MonoBehaviour
{
    //@TODO: Añadir tooltips
    //@TODO: Buscar del upgrade manager cuanto peso permitido
    //@TODO: Desacoplar UI de este codigo
    //@TODO: tener en cuenta que si el jugador coge con el iman los items y los saca floatando desde fuera estaria haciendo trampas, hay que hacer que se tengan en cuenta
    //Ademas el jugador debe tener un peso por default probablemente ( la solucion que se me ocurre es que automaticamente todo lo que tenga
    //Imantado el jugador automaticamente pasa a ser peso del jugador, de esta manera si se sube aun qeu intetne hacer trampas lo sabremos
    [SerializeField] private float MaxAllowedWeight = 200;
    [SerializeField] private float CurrentWeight;
    private bool IsOverweighted = false;

    public TMP_Text WeightTMP;



    public float GetCurrentWeight() { return CurrentWeight; }

    public bool IsElevatorOverweighted () { return IsOverweighted; }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if(WeightTMP == null)
        {
            Debug.LogWarning("Falta asignar el tmp del peso");
        }
        ShowWeightOnUI();
    }

    public void ShowWeightOnUI()
    {
        if (WeightTMP == null)
        {
            return;
        }

        WeightTMP.text = CurrentWeight.ToString() + " / " + MaxAllowedWeight.ToString() + " KG";

    }

    /// <summary>
    /// Registers a carryable candidate when it enters the storage trigger.
    /// </summary>
    /// <param name="Other">Collider entering the trigger.</param>
    private void OnTriggerEnter(Collider Other)
    {
        PhysicsCarryable Carryable = ResolveCarryable(Other);

        if (Carryable == null)
        {
            return;
        }

        OrePickup OrePickup = Other.GetComponent<OrePickup>();

        if (OrePickup == null)
        {
            OrePickup = Other.GetComponentInParent<OrePickup>();
        }

        OreItemData oreItemData = OrePickup.GetOreItemData();
        if (oreItemData == null)
        {
            Debug.LogWarning("Ha entrado un carryable pero su oreItemData es null");
            return;
        }

        AddWeight(oreItemData.GetWeightValue());
    }

    /// <summary>
    /// Releases a carryable automatically if it leaves the storage zone while still externally carried by this zone.
    /// </summary>
    /// <param name="Other">Collider exiting the trigger.</param>
    private void OnTriggerExit(Collider Other)
    {
        PhysicsCarryable Carryable = ResolveCarryable(Other);

        if (Carryable == null)
        {
            return;
        }

        OrePickup OrePickup = Other.GetComponent<OrePickup>();

        if (OrePickup == null)
        {
            OrePickup = Other.GetComponentInParent<OrePickup>();
        }

        OreItemData oreItemData = OrePickup.GetOreItemData();
        if (oreItemData == null)
        {
            Debug.LogWarning("Ha entrado un carryable pero su oreItemData es null");
            return;
        }

        SubstractWeight(oreItemData.GetWeightValue());
    }

    /// <summary>
    /// Function to add weight to current weight
    /// </summary>
    private void AddWeight(float weightToAdd)
    {
        CurrentWeight += weightToAdd;
        CheckIfElevatorIsOverweighted();
    }

    /// <summary>
    /// Function to substract weight to current weight
    /// </summary>
    private void SubstractWeight(float weightToAdd)
    {
        CurrentWeight -= weightToAdd;
        CheckIfElevatorIsOverweighted();
    }

    private void CheckIfElevatorIsOverweighted()
    {
        if(CurrentWeight > MaxAllowedWeight)
        {
            IsOverweighted = true;
        }
        else
        {
            IsOverweighted = false;
        }
        ShowWeightOnUI();
    }

    /// <summary>
    /// Resolves the root PhysicsCarryable from an overlapping collider.
    /// </summary>
    /// <param name="Other">Overlapping collider.</param>
    /// <returns>Resolved PhysicsCarryable or null when not found.</returns>
    private PhysicsCarryable ResolveCarryable(Collider Other)
    {
        if (Other == null)
        {
            return null;
        }

        return Other.GetComponentInParent<PhysicsCarryable>();
    }
}
