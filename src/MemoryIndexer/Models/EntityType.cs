namespace MemoryIndexer.Models;

/// <summary>
/// Types of entities that can be extracted from memories.
/// </summary>
public enum EntityType
{
    /// <summary>
    /// Person name.
    /// </summary>
    Person,

    /// <summary>
    /// Organization or company.
    /// </summary>
    Organization,

    /// <summary>
    /// Location or place.
    /// </summary>
    Location,

    /// <summary>
    /// Date or time expression.
    /// </summary>
    DateTime,

    /// <summary>
    /// Email address.
    /// </summary>
    Email,

    /// <summary>
    /// URL or web address.
    /// </summary>
    Url,

    /// <summary>
    /// Phone number.
    /// </summary>
    Phone,

    /// <summary>
    /// Numeric value (currency, quantity, etc.).
    /// </summary>
    Numeric,

    /// <summary>
    /// Technical term or concept.
    /// </summary>
    Technical,

    /// <summary>
    /// General concept or topic.
    /// </summary>
    Concept,

    /// <summary>
    /// Product or service name.
    /// </summary>
    Product,

    /// <summary>
    /// Event or meeting.
    /// </summary>
    Event,

    /// <summary>
    /// Topic or subject matter.
    /// </summary>
    Topic,

    /// <summary>
    /// Unknown or other type.
    /// </summary>
    Unknown,

    /// <summary>
    /// Unclassified entity.
    /// </summary>
    Other
}
