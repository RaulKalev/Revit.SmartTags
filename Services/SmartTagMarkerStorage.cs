using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;

namespace SmartTags.Services
{
    public static class SmartTagMarkerStorage
    {
        private static readonly Guid SchemaGuid = new Guid("A7B3C4D5-E6F7-8901-2345-6789ABCDEF01");
        private const string SchemaName = "SmartTagsMarker";
        private const string PluginNameFieldName = "PluginName";
        private const string PluginVersionFieldName = "PluginVersion";
        private const string CreationTimestampFieldName = "CreationTimestamp";
        private const string ReferencedElementIdFieldName = "ReferencedElementId";
        private const string ManagedFieldName = "Managed";

        private static Schema _cachedSchema;

        public static Schema EnsureSchema()
        {
            if (_cachedSchema != null)
            {
                return _cachedSchema;
            }

            var existingSchema = Schema.Lookup(SchemaGuid);
            if (existingSchema != null)
            {
                _cachedSchema = existingSchema;
                return _cachedSchema;
            }

            var schemaBuilder = new SchemaBuilder(SchemaGuid);
            schemaBuilder.SetSchemaName(SchemaName);
            schemaBuilder.SetReadAccessLevel(AccessLevel.Public);
            schemaBuilder.SetWriteAccessLevel(AccessLevel.Public);
            schemaBuilder.SetDocumentation("Marks tags created and managed by SmartTags add-in");

            schemaBuilder.AddSimpleField(PluginNameFieldName, typeof(string))
                .SetDocumentation("Name of the plugin that created this tag");

            schemaBuilder.AddSimpleField(PluginVersionFieldName, typeof(string))
                .SetDocumentation("Version of the plugin");

            schemaBuilder.AddSimpleField(CreationTimestampFieldName, typeof(string))
                .SetDocumentation("ISO 8601 timestamp when tag was created");

            schemaBuilder.AddSimpleField(ReferencedElementIdFieldName, typeof(long))
                .SetDocumentation("Element ID of the tagged element");

            schemaBuilder.AddSimpleField(ManagedFieldName, typeof(bool))
                .SetDocumentation("Whether this tag is managed by SmartTags");

            _cachedSchema = schemaBuilder.Finish();
            return _cachedSchema;
        }

        public static void SetManagedTag(IndependentTag tag, ElementId referencedElementId)
        {
            if (tag == null)
            {
                return;
            }

            var schema = EnsureSchema();
            if (schema == null)
            {
                return;
            }

            var entity = new Entity(schema);
            entity.Set(PluginNameFieldName, "SmartTags");
            entity.Set(PluginVersionFieldName, "1.0.0");
            entity.Set(CreationTimestampFieldName, DateTime.UtcNow.ToString("o"));
            entity.Set(ManagedFieldName, true);

            if (referencedElementId != null && referencedElementId != ElementId.InvalidElementId)
            {
                long elementIdValue = GetElementIdValue(referencedElementId);
                entity.Set(ReferencedElementIdFieldName, elementIdValue);
            }
            else
            {
                entity.Set(ReferencedElementIdFieldName, (long)-1);
            }

            tag.SetEntity(entity);
        }

        public static bool IsManagedTag(IndependentTag tag)
        {
            if (tag == null)
            {
                return false;
            }

            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null)
            {
                return false;
            }

            var entity = tag.GetEntity(schema);
            if (!entity.IsValid())
            {
                return false;
            }

            try
            {
                return entity.Get<bool>(ManagedFieldName);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetMetadata(IndependentTag tag, out SmartTagMetadata metadata)
        {
            metadata = null;

            if (tag == null)
            {
                return false;
            }

            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null)
            {
                return false;
            }

            var entity = tag.GetEntity(schema);
            if (!entity.IsValid())
            {
                return false;
            }

            try
            {
                metadata = new SmartTagMetadata
                {
                    PluginName = entity.Get<string>(PluginNameFieldName),
                    PluginVersion = entity.Get<string>(PluginVersionFieldName),
                    CreationTimestamp = entity.Get<string>(CreationTimestampFieldName),
                    Managed = entity.Get<bool>(ManagedFieldName)
                };

                var elementIdValue = entity.Get<long>(ReferencedElementIdFieldName);
                if (elementIdValue >= 0)
                {
                    metadata.ReferencedElementId = CreateElementId(elementIdValue);
                }
                else
                {
                    metadata.ReferencedElementId = ElementId.InvalidElementId;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static long GetElementIdValue(ElementId id)
        {
#if NET8_0_OR_GREATER
            return id.Value;
#else
#pragma warning disable CS0618
            return id.IntegerValue;
#pragma warning restore CS0618
#endif
        }

        private static ElementId CreateElementId(long value)
        {
#if NET8_0_OR_GREATER
            return new ElementId(value);
#else
#pragma warning disable CS0618
            return new ElementId((int)value);
#pragma warning restore CS0618
#endif
        }
    }

    public class SmartTagMetadata
    {
        public string PluginName { get; set; }
        public string PluginVersion { get; set; }
        public string CreationTimestamp { get; set; }
        public ElementId ReferencedElementId { get; set; }
        public bool Managed { get; set; }
    }
}
