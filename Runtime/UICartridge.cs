using System;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// The old name of <see cref="Pack"/>, kept as its base class so that anything typed UICartridge,
    /// such as a serialized field, a LoadAssetAtPath call or a t:UICartridge search, still holds and
    /// finds a pack. Abstract, so CreateInstance&lt;UICartridge&gt;() fails loudly instead of making an
    /// asset with none of a pack's fields.
    /// </summary>
    [Obsolete("UICartridge is now Pack. Create, type and search for Pack (t:Pack).")]
    public abstract class UICartridge : ScriptableObject { }
}
