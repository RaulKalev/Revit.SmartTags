using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartTags.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartTags.Services
{
    public class RevitService
    {
        private Document _doc;

        public RevitService(Document doc)
        {
            _doc = doc;
        }

        public List<ViewSchedule> GetSchedules()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => !s.IsTemplate && !s.IsInternalKeynoteSchedule && !s.IsTitleblockRevisionSchedule)
                .OrderBy(s => s.Name)
                .ToList();
        }


            
        public ScheduleData GetScheduleData(ViewSchedule schedule)
        {
            var data = new ScheduleData();
            if (schedule == null) return data;

            ScheduleDefinition def = schedule.Definition;
            ElementId categoryId = def.CategoryId;

            // map field index to parameter definition
            var fields = new List<ScheduleField>();
            var fieldIds = def.GetFieldOrder();

            foreach (var id in fieldIds)
            {
                ScheduleField field = def.GetField(id);
                // Skip hidden fields? User said "get the parameters of the schedule". 
                // Usually we only show visible cols.
                if (!field.IsHidden)
                {
                    fields.Add(field);
                    data.Columns.Add(field.GetName());
                }
            }

            // Collect elements
            // User requested "filtering is turned off", so we collect from the whole document by category
            // "no grouping" implies itemizing every instance
            
            IList<Element> elements = new List<Element>();
            
            if (categoryId != ElementId.InvalidElementId)
            {
                elements = new FilteredElementCollector(_doc)
                    .OfCategoryId(categoryId)
                    .WhereElementIsNotElementType()
                    .ToElements();
            }
            // ... fallback logic omitted for brevity as it was empty ...

            // Ensure "ElementId" and "TypeName" are known columns for internal use
            if (!data.Columns.Contains("ElementId"))
            {
                data.Columns.Insert(0, "ElementId");
            }
            if (!data.Columns.Contains("TypeName"))
            {
                data.Columns.Insert(1, "TypeName");
            }

            foreach (Element el in elements)
            {
                var rowData = new List<string>();
                // Add ElementId
#if NET8_0_OR_GREATER
                rowData.Add(el.Id.Value.ToString());
#else
#pragma warning disable CS0618
                rowData.Add(el.Id.IntegerValue.ToString());
#pragma warning restore CS0618
#endif
                
                // Add TypeName
                ElementId typeId = el.GetTypeId();
                string typeName = "";
                if (typeId != ElementId.InvalidElementId)
                {
                    Element typeElem = _doc.GetElement(typeId);
                    if (typeElem != null) typeName = typeElem.Name;
                }
                rowData.Add(typeName);

                foreach (ScheduleField field in fields)
                {
                    string val = "";
                    bool isType = false;
                    
                    // Special Handling
                    if (field.ParameterId == ElementId.InvalidElementId)
                    {
                        val = ""; 
                    }
                    else
                    {
                        var result = GetParameterValue(el, field.ParameterId);
                        val = result.Item1;
                        isType = result.Item2;
                    }
                    
                    if (val == null) val = "";
                    rowData.Add(val);

                    // Store type info for this column (aggregate if needed, but usually consistent per column)
                    string colName = field.GetName();
                    if (!data.IsTypeParameter.ContainsKey(colName))
                    {
                        data.IsTypeParameter[colName] = isType;
                    }
                    // If any instance turns out to be type based (fallback), maybe mark as type?
                    // But usually a schedule field is consistent. 
                    // However, our GetParameterValue has a fallback.
                    // If the field definition is definitely Instance or Type, we should respect that, but ScheduleField doesn't easily say.
                    // We'll rely on where we found the data. 
                    if (isType) data.IsTypeParameter[colName] = true; 
                }
                data.Rows.Add(rowData);
            }

            return data;
        }

        private (string, bool) GetParameterValue(Element el, ElementId parameterId)
        {
            Parameter p = null;
            bool isType = false;
            
#if NET8_0_OR_GREATER
            long idValue = parameterId.Value;
#else
#pragma warning disable CS0618
            int idValue = parameterId.IntegerValue;
#pragma warning restore CS0618
#endif

            // 1. Try BuiltInParameter on Instance
            if (idValue < 0)
            {
                p = el.get_Parameter((BuiltInParameter)idValue);
            }
            // 2. Try normal get_Parameter or handle Shared Parameters on Instance
            else
            {
                try
                {
                    var paramElem = _doc.GetElement(parameterId);
                    if (paramElem != null)
                    {
                        p = el.LookupParameter(paramElem.Name);
                    }
                }
                catch { }
            }

            // 3. If missing on instance, check Type
            if (p == null)
            {
                ElementId typeId = el.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    Element typeElem = _doc.GetElement(typeId);
                    if (typeElem != null)
                    {
                         // Check Type
                         if (idValue < 0)
                            p = typeElem.get_Parameter((BuiltInParameter)idValue);
                         else
                         {
                             var paramElem = _doc.GetElement(parameterId);
                             if (paramElem != null) p = typeElem.LookupParameter(paramElem.Name);
                         }
                         
                         if (p != null) isType = true;
                    }
                }
            }

            if (p != null)
            {
                string val;
                if (p.StorageType == StorageType.String)
                    val = p.AsString();
                else
                    val = p.AsValueString();
                
                return (val, isType);
            }

            return ("", false);
        }


        public List<SheetItem> GetSheets()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate)
                .Select(s => new SheetItem(s))
                .OrderBy(s => s.SheetNumber)
                .ToList();
        }
    }
}
