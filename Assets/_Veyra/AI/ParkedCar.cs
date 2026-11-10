using UnityEngine;

namespace VeyraCity.AI
{
    /// <summary>A parked car (see <see cref="ParkedCars"/>) stays put until it is hit hard enough, then it becomes a physics body.</summary>
    public sealed class ParkedCar : MonoBehaviour
    {
        void OnCollisionEnter(Collision c)
        {
            var rb = GetComponent<Rigidbody>();
            if (rb != null && rb.isKinematic && c.relativeVelocity.sqrMagnitude > 9f) rb.isKinematic = false;
        }
    }
}
