using UnityEngine;
using UnityEngine.Events;

// Aracı belirli bir olaya kadar yerinde tutar.
// Rigidbody "kinematik" yapılınca fizik aracı itemez: yerçekimi, çarpışma ve rüzgâr etkisiz kalır, araç olduğu yerde durur.
// Halat araca bağlanınca halat sistemi ReleaseHold() metodunu çağıracak; o andan itibaren araç normal fizikle hareket eder.
[RequireComponent(typeof(Rigidbody))]
public class VehicleHold : MonoBehaviour
{
    [Tooltip("Açıksa araç sahne başlarken kinematik (sabit) olur; ReleaseHold() çağrılana kadar yerinde kalır.")]
    [SerializeField] private bool holdOnStart = true;

    [Tooltip("ReleaseHold() çağrıldığında tetiklenir (ileride ses veya efekt bağlamak için).")]
    public UnityEvent onReleased;

    private Rigidbody body;

    // Araç şu an sabit tutuluyor mu? (Kinematik = tutuluyor.)
    public bool IsHeld
    {
        get { return body != null && body.isKinematic; }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (holdOnStart)
            body.isKinematic = true;
    }

    // Aracı serbest bırakır: fizik açılır ve Rigidbody uyandırılır (uyuyan Rigidbody hareket etmeyebilir).
    // Halat bağlandığında halat sistemi bu metodu çağıracak.
    [ContextMenu("Bırak (test)")]
    public void ReleaseHold()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();
        if (!body.isKinematic)
            return; // zaten serbest, tekrar tetikleme

        body.isKinematic = false;
        body.WakeUp();
        if (onReleased != null)
            onReleased.Invoke();
    }
}
