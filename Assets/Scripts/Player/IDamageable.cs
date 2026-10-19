using UnityEngine;

// U3: hasar alabilen şey (oyuncu; ileride hırsız). Hırsızlar (U4) yalnız bu arayüzü bilir.
public interface IDamageable
{
    // amount: can puanı; source: vuran (sendeleme yönü için; null olabilir). Hasar işlendiyse true.
    bool TakeDamage(float amount, Transform source);
}
