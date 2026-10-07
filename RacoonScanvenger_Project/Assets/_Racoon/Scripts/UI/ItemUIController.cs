using Racoon.Items;
using Racoon.Player;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ItemUIController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image mainItemImage;
    [SerializeField] private Image reserveItemImage;

    [Header("Roulette")]
    [SerializeField] private List<ItemData> possibleItems;

    [SerializeField] private float rouletteDuration = 1.5f;
    [SerializeField] private float initialSpeed = 0.05f;
    [SerializeField] private float finalSpeed = 0.18f;

    private PlayerInventory inventory;

    private Coroutine mainRoulette;
    private Coroutine reserveRoulette;

    private bool mainRoulettePlaying;
    private bool reserveRoulettePlaying;

    private void Start()
    {
        ClearMainItem();
        ClearReserveItem();
    }

    /// <summary>
    /// Conecta esta UI con el inventario del jugador local.
    /// </summary>
    public void Bind(PlayerInventory newInventory)
    {
        // Si ya estábamos escuchando otro inventario,
        // dejamos de escuchar sus eventos.
        if (inventory != null)
        {
            inventory.ItemAdded -= OnItemAdded;
            inventory.InventoryChanged -= OnInventoryChanged;
        }

        inventory = newInventory;

        if (inventory == null)
            return;

        // Escuchamos los cambios del inventario del jugador.
        inventory.ItemAdded += OnItemAdded;
        inventory.InventoryChanged += OnInventoryChanged;

        RefreshUI();
    }

    private void OnDestroy()
    {
        // Evitamos dejar eventos conectados cuando se destruye la UI.
        if (inventory != null)
        {
            inventory.ItemAdded -= OnItemAdded;
            inventory.InventoryChanged -= OnInventoryChanged;
        }
    }

    /// <summary>
    /// Se llama cuando entra un nuevo objeto en el inventario.
    /// Decide si la ruleta debe aparecer en el hueco principal
    /// o en el hueco de reserva.
    /// </summary>
    private void OnItemAdded(int slot, ItemData item)
    {
        if (inventory == null)
            return;

        // Si es el primer objeto, siempre va al hueco grande.
        if (inventory.Count == 1)
        {
            PlayMainRoulette(item);
            return;
        }

        // Si el objeto añadido es el que está equipado,
        // hacemos la ruleta en el hueco principal.
        if (slot == inventory.EquippedSlot)
        {
            PlayMainRoulette(item);
        }
        else
        {
            // Si ya llevábamos un objeto equipado,
            // el nuevo va a la reserva.
            PlayReserveRoulette(item);
        }
    }

    /// <summary>
    /// Actualiza la UI cuando cambia el contenido del inventario.
    /// </summary>
    private void OnInventoryChanged()
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (inventory == null)
            return;

        ItemData mainItem = null;
        ItemData reserveItem = null;

        // El objeto equipado siempre aparece en el cuadrado grande.
        if (inventory.HasEquipped)
        {
            mainItem = inventory.EquippedItem;

            // Buscamos el otro objeto del inventario para mostrarlo
            // en el cuadrado pequeño.
            for (int i = 0; i < inventory.Count; i++)
            {
                if (i != inventory.EquippedSlot)
                {
                    reserveItem = inventory.GetItemAt(i);
                    break;
                }
            }
        }
        else
        {
            // Por seguridad, si tenemos objetos pero ninguno equipado,
            // mostramos el primero como principal.
            mainItem = inventory.GetItemAt(0);
            reserveItem = inventory.GetItemAt(1);
        }

        // Mientras una ruleta esté funcionando no sobrescribimos
        // su imagen con el resultado real.
        if (!mainRoulettePlaying)
            SetMainItem(mainItem);

        if (!reserveRoulettePlaying)
            SetReserveItem(reserveItem);
    }

    public void PlayMainRoulette(ItemData finalItem)
    {
        if (mainRoulette != null)
            StopCoroutine(mainRoulette);

        mainRoulettePlaying = true;

        mainRoulette = StartCoroutine(
            Roulette(mainItemImage, finalItem, true)
        );
    }

    public void PlayReserveRoulette(ItemData finalItem)
    {
        if (reserveRoulette != null)
            StopCoroutine(reserveRoulette);

        reserveRoulettePlaying = true;

        reserveRoulette = StartCoroutine(
            Roulette(reserveItemImage, finalItem, false)
        );
    }

    private IEnumerator Roulette(Image image, ItemData finalItem, bool isMain)
    {
        image.enabled = true;

        float elapsedTime = 0f;
        int index = 0;

        while (elapsedTime < rouletteDuration)
        {
            if (possibleItems.Count > 0)
            {
                image.sprite = possibleItems[index].icon;

                index++;

                if (index >= possibleItems.Count)
                    index = 0;
            }

            float normalizedTime = elapsedTime / rouletteDuration;

            float delay = Mathf.Lerp(
                initialSpeed,
                finalSpeed,
                normalizedTime
            );

            yield return new WaitForSeconds(delay);

            elapsedTime += delay;
        }

        // Al terminar la animación mostramos
        // el objeto que REALMENTE ha tocado.
        image.sprite = finalItem.icon;

        if (isMain)
        {
            mainRoulettePlaying = false;
            mainRoulette = null;
        }
        else
        {
            reserveRoulettePlaying = false;
            reserveRoulette = null;
        }

        RefreshUI();
    }

    public void SetMainItem(ItemData item)
    {
        if (item == null)
        {
            ClearMainItem();
            return;
        }

        mainItemImage.enabled = true;
        mainItemImage.sprite = item.icon;
    }

    public void SetReserveItem(ItemData item)
    {
        if (item == null)
        {
            ClearReserveItem();
            return;
        }

        reserveItemImage.enabled = true;
        reserveItemImage.sprite = item.icon;
    }

    public void ClearMainItem()
    {
        mainItemImage.sprite = null;
        mainItemImage.enabled = false;
    }

    public void ClearReserveItem()
    {
        reserveItemImage.sprite = null;
        reserveItemImage.enabled = false;
    }
}