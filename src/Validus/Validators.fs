namespace Validus

open System

/// A validation message for a field
type ValidationMessage = string -> string

/// Given a value, return true/false to indicate validity
type ValidationRule<'a> = 'a -> bool

/// Given a field name and value, 'a, produces a ValidationResult<'a>
type Validator<'a, 'b> = string -> 'a -> ValidationResult<'b>

/// Validation rules
module ValidationRule =
    let inline equality<'a when 'a : equality> (equalTo : 'a) : ValidationRule<'a> =
        fun v -> v = equalTo

    let inline inequality<'a when 'a : equality> (notEqualTo : 'a) : ValidationRule<'a> =
        fun v -> not(v = notEqualTo)

    let inline between<'a when 'a : comparison> (min : 'a) (max : 'a) : ValidationRule<'a> =
        fun v -> v >= min && v <= max

    let inline greaterThan<'a when 'a : comparison> (min : 'a) : ValidationRule<'a> =
        fun v -> v > min

    let inline lessThan<'a when 'a : comparison> (max : 'a) : ValidationRule<'a> =
        fun v -> v < max

    let inline betweenLen (min : int) (max : int) (x : ^a) : bool =
        (between min max (^a : (member Length : int) x))

    let inline equalsLen (len : int) (x : ^a) : bool =
        (equality len (^a : (member Length : int) x))

    let inline greaterThanLen (min : int) (x : ^a) : bool =
        (greaterThan min (^a : (member Length : int) x))

    let inline lessThanLen (max : int) (x : ^a) : bool =
        (lessThan max (^a : (member Length : int) x))

    let inline strPattern (pattern : string) : ValidationRule<string> =
        fun v -> if isNull v then false else Text.RegularExpressions.Regex.IsMatch(v, pattern)

/// Functions for Validator type
module Validator =
    /// Create a new Validator
    let create
        (message : ValidationMessage)
        (rule : 'a -> bool)
        : Validator<'a, 'a> =
        fun (field : string) (value : 'a) ->
            let error = ValidationErrors.create field [ message field ]
            if rule value then Ok value
            else error |> Error

type ValidatorGroup<'a>(startValidator : Validator<'a, 'a>) =
    member _.Build() = startValidator

    member _.And(andValidator : Validator<'a, 'a>) =
        ValidatorGroup(fun f v ->
            match startValidator f v, andValidator f v with
            | Ok a, Ok _   -> Ok a
            | Error e, Ok _   -> Error e
            | Ok _, Error e   -> Error e
            | Error e1, Error e2 -> Error (ValidationErrors.merge e1 e2))

    member _.Then(nextValidator : Validator<'a, 'a>) =
        ValidatorGroup(fun f v ->
            Result.bind (nextValidator f) (startValidator f v))

/// Validation functions
module Validators =
    type EqualityValidator<'a when 'a : equality>() =
        /// Value is equal to provided value
        member _.equals
            (equalTo : 'a)
            (message : ValidationMessage)
            (field : string)
            (input : 'a)
            : ValidationResult<'a> =
            let rule = ValidationRule.equality equalTo
            Validator.create message rule field input

        /// Value is not equal to provided value
        member _.notEquals
            (notEqualTo : 'a)
            (message : ValidationMessage)
            (field : string)
            (input : 'a)
            : ValidationResult<'a> =
            let rule = ValidationRule.inequality notEqualTo
            Validator.create message rule field input

    type ComparisonValidator<'a when 'a : comparison>() =
        inherit EqualityValidator<'a>()

        /// Value is inclusively between provided min and max
        member _.between
            (min : 'a)
            (max : 'a)
            (message : ValidationMessage)
            (field : string)
            (input : 'a)
            : ValidationResult<'a> =
            let rule = ValidationRule.between min max
            Validator.create message rule field input

        /// Value is greater than provided min
        member _.greaterThan
            (min : 'a)
            (message : ValidationMessage)
            (field : string)
            (input : 'a)
            : ValidationResult<'a> =
            let rule = ValidationRule.greaterThan min
            Validator.create message rule field input

        /// Value is less than provided max
        member _.lessThan
            (max : 'a)
            (message : ValidationMessage)
            (field : string)
            (input : 'a)
            : ValidationResult<'a> =
            let rule = ValidationRule.lessThan max
            Validator.create message rule field input

    type StringValidator() =
        inherit EqualityValidator<string>()

        /// Validate string is between length (inclusive)
        member _.betweenLen
            (min : int)
            (max : int)
            (message : ValidationMessage)
            (field : string)
            (input : string)
            : ValidationResult<string> =
            let rule = ValidationRule.betweenLen min max
            Validator.create message rule field input

        /// Validate string is null or ""
        member _.empty
            (message : ValidationMessage)
            (field : string)
            (input : string)
            : ValidationResult<string> =
            Validator.create message String.IsNullOrWhiteSpace field input

        /// Validate string length is equal to provided value
        member _.equalsLen
            (len : int)
            (message : ValidationMessage)
            (field : string)
            (input : string)
            : ValidationResult<string> =
            let rule = ValidationRule.equalsLen len
            Validator.create message rule field input

        /// Validate string length is greater than provided value
        member _.greaterThanLen
            (min : int)
            (message : ValidationMessage)
            (field : string)
            (input : string)
            : ValidationResult<string> =
            let rule = ValidationRule.greaterThanLen min
            Validator.create message rule field input

        /// Validate string length is less than provided value
        member _.lessThanLen
            (max : int)
            (message : ValidationMessage)
            (field : string)
            (input : string)
            : ValidationResult<string> =
            let rule = ValidationRule.lessThanLen max
            Validator.create message rule field input

        /// Validate string is not null or ""
        member _.notEmpty
            (message : ValidationMessage)
            (field : string)
            (input : string)
            : ValidationResult<string> =
            Validator.create
                message
                (fun str -> not(String.IsNullOrWhiteSpace (str)))
                field
                input

        /// Validate string matches regular expression
        member _.pattern
            (pattern : string)
            (message : ValidationMessage)
            (field : string)
            (input : string)
            : ValidationResult<string> =
            let rule = ValidationRule.strPattern pattern
            Validator.create message rule field input

    type GuidValidator() =
        inherit EqualityValidator<Guid> ()

        /// Validate string is null or ""
        member _.empty
            (message : ValidationMessage)
            (field : string)
            (input : Guid)
            : ValidationResult<Guid> =
            Validator.create message (fun guid -> Guid.Empty = guid) field input

        /// Validate string is not null or ""
        member _.notEmpty
            (message : ValidationMessage)
            (field : string)
            (input : Guid)
            : ValidationResult<Guid> =
            Validator.create message (fun guid -> Guid.Empty <> guid) field input

    type ListValidator<'a when 'a : equality>() =
        inherit EqualityValidator<'a list> ()

        /// Validate list is between length (inclusive)
        member _.betweenLen
            (min : int)
            (max : int)
            (message : ValidationMessage)
            (field : string)
            (input : ('a) list)
            : ValidationResult<'a list> =
            let rule = ValidationRule.betweenLen min max
            Validator.create message rule field input

        /// Validate list is empty
        member _.empty
            (message : ValidationMessage)
            (field : string)
            (input : ('a) list)
            : ValidationResult<'a list> =
            Validator.create message List.isEmpty field input

        /// Validate list length is equal to provided value
        member _.equalsLen
            (len : int)
            (message : ValidationMessage)
            (field : string)
            (input : ('a) list)
            : ValidationResult<'a list> =
            let rule = ValidationRule.equalsLen len
            Validator.create message rule field input

        /// Validate list contains element matching predicate
        member _.exists
            (predicate : 'a -> bool)
            (message : ValidationMessage)
            (field : string)
            (input : ('a) list)
            : ValidationResult<('a) list> =
            Validator.create message (List.exists predicate) field input

        /// Validate list length is greater than provided value
        member _.greaterThanLen
            (min : int)
            (message : ValidationMessage)
            (field : string)
            (input : ('a) list)
            : ValidationResult<'a list> =
            let rule = ValidationRule.greaterThanLen min
            Validator.create message rule field input

        /// Validate list length is less than provided value
        member _.lessThanLen
            (max : int)
            (message : ValidationMessage)
            (field : string)
            (input : ('a) list)
            : ValidationResult<'a list> =
            let rule = ValidationRule.lessThanLen max
            Validator.create message rule field input

        /// Validate list is not empty
        member _.notEmpty
            (message : ValidationMessage)
            (field : string)
            (input : ('a) list)
            : ValidationResult<'a list> =
            Validator.create message (fun x -> not(List.isEmpty x)) field input

    module Default =
        type DefaultEqualityValidator<'a when 'a
            : equality>(x : EqualityValidator<'a>) =
            /// Value is equal to provided value with the default error message
            member _.equals (equalTo: 'a) (field : string) (input : 'a) =
                let msg field = sprintf "'%s' must be equal to %A" field equalTo
                x.equals equalTo msg field input

            /// Value is not equal to provided value with the default
            /// error message
            member _.notEquals (notEqualTo : 'a) (field : string) (input : 'a) =
                let msg field = sprintf "'%s' must not equal %A" field notEqualTo
                x.notEquals notEqualTo msg field input

        type DefaultComparisonValidator<'a when 'a
            : comparison>(x : ComparisonValidator<'a>) =
            inherit DefaultEqualityValidator<'a>(x)

            /// Value is inclusively between provided min and max with the
            /// default error message
            member _.between (min : 'a) (max : 'a) (field : string) (input : 'a) =
                let msg field =
                    sprintf "'%s' must be between %A and %A" field min max
                x.between min max msg field input

            /// Value is greater than provided min with the default error
            /// message
            member _.greaterThan (min : 'a) (field : string) (input : 'a) =
                let msg field =
                    sprintf "'%s' must be greater than %A" field min
                x.greaterThan min msg field input

            /// Value is less than provided max with the default error message
            member _.lessThan (max : 'a) (field : string) (input : 'a) =
                let msg field =
                    sprintf "'%s' must be less than %A" field max
                x.lessThan max msg field input

        type DefaultStringValidator(x : StringValidator) =
            inherit DefaultEqualityValidator<string>(x)

            /// Validate string is between length (inclusive) with the default
            /// error message
            member _.betweenLen (min : int) (max : int) (field : string) (input : string) =
                let msg field =
                    sprintf
                        "'%s' must be between %i and %i characters"
                        field min max
                x.betweenLen min max msg field input

            /// Validate string is null or "" with the default error message
            member _.empty (field : string) (input : string) =
                let msg field = sprintf "'%s' must be empty" field
                x.empty msg field input

            /// Validate string length is greater than provided value with the
            /// default error message
            member _.equalsLen (len : int) (field : string) (input : string) =
                let msg field = sprintf "'%s' must be %i characters" field len
                x.equalsLen len msg field input

            /// Validate string length is greater than provided value with the
            /// default error message
            member _.greaterThanLen (min : int) (field : string) (input : string) =
                let msg field =
                    sprintf "'%s' must not execeed %i characters" field min
                x.greaterThanLen min msg field input

            /// Validate string length is less than provided value with the
            /// default error message
            member _.lessThanLen (max : int) (field : string) (input : string) =
                let msg field =
                    sprintf "'%s' must be at less than %i characters" field max
                x.lessThanLen max msg field input

            /// Validate string is not null or "" with the default error message
            member _.notEmpty (field : string) (input : string) =
                let msg field = sprintf "'%s' must not be empty" field
                x.notEmpty msg field input

            /// Validate string matches regular expression with the default
            /// error message
            member _.pattern (pattern : string) (field : string) (input : string) =
                let msg field = sprintf "'%s' must match pattern %s" field pattern
                x.pattern pattern msg field input

        type DefaultGuidValidator(x : GuidValidator) =
            inherit DefaultEqualityValidator<Guid>(x)

            /// Validate System.Guid is null or "" with the default error
            /// message
            member _.empty (field : string) (input : Guid) =
                let msg field = sprintf "'%s' must be empty" field
                x.empty msg field input

            /// Validate System.Guid is not null or "" with the default error
            /// message
            member _.notEmpty (field : string) (input : Guid) =
                let msg field = sprintf "'%s' must not be empty" field
                x.notEmpty msg field input

        type DefaultListValidator<'a when 'a : equality>(x : ListValidator<'a>) =
            inherit DefaultEqualityValidator<'a list>(x)

            /// Validate string is between length (inclusive) with the default
            /// error message
            member _.betweenLen (min : int) (max : int) (field : string) (input : 'a list) =
                let msg field =
                    sprintf
                        "'%s' must be between %i and %i items in length"
                        field min max
                x.betweenLen min max msg field input

            /// Validate string is null or "" with the default error message
            member _.empty (field : string) (input : 'a list) =
                let msg field = sprintf "'%s' must be empty" field
                x.empty msg field input

            /// Validate string length is greater than provided value with the
            /// default error message
            member _.equalsLen (len : int) (field : string) (input : 'a list) =
                let msg field = sprintf "'%s' must be %i items in length" field len
                x.equalsLen len msg field input

            /// Validate string length is greater than provided value with the
            /// default error message
            member _.exists (predicate : 'a -> bool) (field : string) (input : 'a list) =
                let msg field = sprintf "'%s' must contain the specified item" field
                x.exists predicate msg field input

            /// Validate string length is greater than provided value with the
            /// default error message
            member _.greaterThanLen (min : int) (field : string) (input : 'a list) =
                let msg field =
                    sprintf "'%s' must not execeed %i items in length" field min
                x.greaterThanLen min msg field input

            /// Validate string length is less than provided value with the
            /// default error message
            member _.lessThanLen (max : int) (field : string) (input : 'a list) =
                let msg field =
                    sprintf "'%s' must be at least %i items in length" field max
                x.lessThanLen max msg field input

            /// Validate string is not null or "" with the default error message
            member _.notEmpty (field : string) (input : 'a list) =
                let msg field = sprintf "'%s' must not be empty" field
                x.notEmpty msg field input
