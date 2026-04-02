using UnityEngine;

public class OneSymbol : MonoBehaviour
{
    [SerializeField] private int symbolNumber;
    [SerializeField] private int ammount;

    public int GetSymbolNumber()
    {
        return symbolNumber;
    }

    public int GetAmount()
    {
        return ammount;
    }

}
