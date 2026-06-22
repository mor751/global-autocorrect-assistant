namespace Autocorrect.Core.Brain;

public static class TreeSitterQueries
{
    public static string SymbolsFor(string languageKey) => languageKey switch
    {
        "typescript" or "javascript" => """
            (function_declaration name: (identifier) @symbol) @unit
            (class_declaration name: (identifier) @symbol) @unit
            (method_definition name: (property_identifier) @symbol) @unit
            (lexical_declaration (variable_declarator name: (identifier) @symbol value: (arrow_function)) @unit)
            (export_statement declaration: (function_declaration name: (identifier) @symbol) @unit)
            (export_statement declaration: (class_declaration name: (identifier) @symbol) @unit)
            """,
        "python" => """
            (function_definition name: (identifier) @symbol) @unit
            (class_definition name: (identifier) @symbol) @unit
            """,
        "csharp" => """
            (class_declaration name: (identifier) @symbol) @unit
            (interface_declaration name: (identifier) @symbol) @unit
            (struct_declaration name: (identifier) @symbol) @unit
            (method_declaration name: (identifier) @symbol) @unit
            (property_declaration name: (identifier) @symbol) @unit
            """,
        "go" => """
            (function_declaration name: (identifier) @symbol) @unit
            (method_declaration name: (field_identifier) @symbol) @unit
            (type_declaration (type_spec name: (type_identifier) @symbol)) @unit
            """,
        "rust" => """
            (function_item name: (identifier) @symbol) @unit
            (struct_item name: (type_identifier) @symbol) @unit
            (impl_item) @unit
            """,
        "java" => """
            (class_declaration name: (identifier) @symbol) @unit
            (method_declaration name: (identifier) @symbol) @unit
            (interface_declaration name: (identifier) @symbol) @unit
            """,
        _ => string.Empty
    };

    public static string ImportsFor(string languageKey) => languageKey switch
    {
        "typescript" or "javascript" => """
            (import_statement source: (string (string_fragment) @import))
            (import_statement source: (string) @import)
            """,
        "python" => """
            (import_statement name: (dotted_name) @import)
            (import_from_statement module_name: (dotted_name) @import)
            """,
        "csharp" => """
            (using_directive name: (identifier) @import)
            (using_directive name: (qualified_name) @import)
            """,
        "go" => """
            (import_spec path: (interpreted_string_literal) @import)
            """,
        "rust" => """
            (use_declaration argument: (_) @import)
            """,
        "java" => """
            (import_declaration (_) @import)
            """,
        _ => string.Empty
    };

    public static string CallsFor(string languageKey) => languageKey switch
    {
        "typescript" or "javascript" => """
            (call_expression function: (identifier) @call)
            (call_expression function: (member_expression property: (property_identifier) @call))
            """,
        "python" => """
            (call function: (identifier) @call)
            (call function: (attribute attribute: (identifier) @call))
            """,
        "csharp" => """
            (invocation_expression function: (identifier) @call)
            (invocation_expression function: (member_access_expression name: (identifier) @call))
            """,
        "go" => """
            (call_expression function: (identifier) @call)
            (call_expression function: (selector_expression field: (field_identifier) @call))
            """,
        "rust" => """
            (call_expression function: (identifier) @call)
            (call_expression function: (field_expression field: (field_identifier) @call))
            """,
        "java" => """
            (method_invocation name: (identifier) @call)
            """,
        _ => string.Empty
    };
}
