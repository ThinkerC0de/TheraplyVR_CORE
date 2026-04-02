using System.Collections;
using TheraplyVR.Pairs.Basic;
using UnityEngine;

public class TwoHandsClock : TwoHands
{
    //Dobrze by³oby wykorzystaæ klasê abstrakcyjn¹ TwoHand() aby wszystkie sesjê by³y traktowane tak samo.
    //Sesja z zegarem jest najmniej podobna do pozosta³ych. Wiêc tutaj trzeba trochê inaczej rozwi¹zaæ rozgrywkê.
    //Pozosta³e s¹ podobne do tego co jest ju¿ zrobionê wiêc mi bêdzie ³atwiej to zaimplementowaæ.

    //Jako Symbol_middle mo¿emy daæ zegar cyfrowy np jako label
    //Jako Place_L mo¿emy ustawiæ zegar ze wskazówkami
    //Dziêki temu mozemy korzystaæ z funkcji do ukrywania tych obiektów z klasy abstrakcyjnej jak:
    //HideAndShowObjects(), ShowTemplate(bool state)




    protected override void Session()
    {
        //Rozpocznij grê na bazie za³adowanego StartSession(BasicSessionConfig config)
        //TimeUpdate() resetuje czas danej rozgrywki
    }

    public override void ShowHint()
    {
        //Wyœwietlaj podpowiedzi gdy 2 b³êdne ruchy
    }

    public override IEnumerator Check(bool isLeft, Symbol selected)
    {
        //sprawdzanie pozycji dla lewego i prawego symbolu osobno - w przypadku zegera sprawdzamy pozycjê jednej i drugiej wskazówki
        //istotny jest czas reakcji dziecka (czas miedzy pokazaniem wskazówki a prawid³owym zaznaczeniem odpowiedzi)
        //Przy czym wynik graczowi pokazujemy z opuŸniniem 3 sec, w tym czasie musi utrzymaæ zaznaczenie w odpowiednim miejscu.

        //Repeat() wywo³ywane gdy zaliczona runda
        //Na koniec rozgrywki wysy³amy result (wbudowane ju¿ w Repeat())
        //manager.GameFinished(result);


        //mo¿esz to rozwi¹zaæ po swojemu jeœli bêdzie bardziej pasowa³o do Zegara

        yield return new WaitForSeconds(1);
    }


}
