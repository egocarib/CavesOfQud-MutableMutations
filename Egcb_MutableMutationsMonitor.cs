using System;
using System.Collections;
using UnityEngine;
using XRL.World.Parts;

public class Egcb_MutableMutationsMonitor : MonoBehaviour
{
    [NonSerialized] public static Egcb_MutableMutationsMonitor Instance;
    private static bool bInitialized;

    public bool Initialize()
    {
        if (!Egcb_MutableMutationsMonitor.bInitialized)
        {
            Egcb_MutableMutationsMonitor.bInitialized = true;
            Egcb_MutableMutationsMonitor.Instance = this;
            base.StartCoroutine(this.OptionMonitorLoop());
            return true;
        }
        return false;
    }

    public static bool IsActive
    {
        get
        {
            return Egcb_MutableMutationsMonitor.bInitialized;
        }
    }

    private IEnumerator OptionMonitorLoop()
    {
        Debug.Log("Mutable Mutations Mod - Initiated Options monitor.");
        for (;;)
        {
            if (GameManager.Instance.CurrentGameView == "Options")
            {
                do { yield return new WaitForSeconds(0.2f); } while (GameManager.Instance.CurrentGameView == "Options");
                Egcb_MutableMutations.ReapplyOptions();
            }
            yield return new WaitForSeconds(1f);
        }
    }

}
