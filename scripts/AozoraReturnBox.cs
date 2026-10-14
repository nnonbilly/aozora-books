// 返却箱: 本が触れると、プールの本は消え (プールに戻る)、固定の本は元の場所に戻る。
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AozoraReturnBox : UdonSharpBehaviour
{
    public AozoraLibrary library;

    void OnTriggerEnter(Collider other)
    {
        if (other == null || library == null) return;
        AozoraBook b = other.GetComponent<AozoraBook>();
        if (b == null) return;
        if (!Networking.IsOwner(b.gameObject)) return; // 本の所有者だけが処理する
        library._ReturnBook(b);
    }
}
