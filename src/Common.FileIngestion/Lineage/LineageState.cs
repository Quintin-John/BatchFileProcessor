namespace Common.FileIngestion.Lineage;

/// <summary>
/// A record's position in the ingestion lifecycle (design §8). Every record emits a lineage event at
/// each transition: <see cref="Consumed"/> → <see cref="Accepted"/> | <see cref="Rejected"/> →
/// <see cref="Batched"/> → <see cref="Published"/> → <see cref="Confirmed"/> | <see cref="Failed"/>; a
/// control record the layout marks skip is terminal at <see cref="Skipped"/>.
/// This is a forensic trace of how a record moved, not the system of record.
/// </summary>
public enum LineageState
{
    /// <summary>Framed from the source stream.</summary>
    Consumed,

    /// <summary>Parsed and mapped successfully.</summary>
    Accepted,

    /// <summary>Failed field validation; routed to the reject queue.</summary>
    Rejected,

    /// <summary>Placed into a batch message.</summary>
    Batched,

    /// <summary>Its batch was published to the broker.</summary>
    Published,

    /// <summary>Its batch was confirmed by the broker.</summary>
    Confirmed,

    /// <summary>Its batch's publish/confirm failed terminally.</summary>
    Failed,

    /// <summary>Recognised as a control record the layout marks skip; consumed for framing, never emitted.</summary>
    Skipped,
}
