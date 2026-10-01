using System;

namespace OneJS {
    /// <summary>The old name of <see cref="Pack"/>, kept so code written against it still compiles.</summary>
    [Obsolete("UICartridge is now Pack. Assets made as UICartridge load as Pack, so load, cast and search for Pack (t:Pack).")]
    public class UICartridge : Pack { }
}
