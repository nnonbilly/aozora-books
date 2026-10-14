// 開いた本のページのクリック判定。Interact で本の NextPage / PrevPage を呼ぶ。
// 本 (Pickup) の子にすると掴む操作と競合するので、本の外に置いて本に追従させている。
using UdonSharp;
using UnityEngine;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AozoraPageButton : UdonSharpBehaviour
{
    public AozoraBook book;
    public string eventName = "NextPage";

    public override void Interact()
    {
        if (book != null) book.SendCustomEvent(eventName);
    }
}
