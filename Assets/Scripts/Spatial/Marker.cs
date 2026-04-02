using UnityEngine;

public class Marker : MonoBehaviour
{
    [SerializeField] private bool isLeft;
    
    private Symbol symbol;
    private Symbol oldSymbol;
    private bool goodSide;

    // Aktywacja VFX i zmiana koloru na niebieski
    private void OnTriggerEnter(Collider other)
    {
        if (enabled && other.tag == "Symbol")
        {
            symbol = other.GetComponent<Symbol>();
            goodSide = isLeft == symbol.isLeft;

            if (goodSide)
            {
                symbol.Activate();
            }
        }
    }

    //Deactivate VFX
    private void OnTriggerExit(Collider other)
    {
        if (enabled && other.tag == "Symbol")
        {
            oldSymbol = other.GetComponent<Symbol>();

            Debug.Log("----> Deactivate trigger " + oldSymbol.transform.parent.name);
            oldSymbol.Deactivate();        
        }
    }


}
