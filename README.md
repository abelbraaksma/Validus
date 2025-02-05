# Validus

[![NuGet Version](https://img.shields.io/nuget/v/Validus.svg)](https://www.nuget.org/packages/Validus)
[![build](https://github.com/pimbrouwers/Validus/actions/workflows/build.yml/badge.svg)](https://github.com/pimbrouwers/Validus/actions/workflows/build.yml)

Validus is a composable validation library for F#, with built-in validators for most primitive types and easily extended through custom validators.

## Key Features

- [Composable](#combining-validators) validation.
- [Built-in](#built-in-validators) validators for most primitive types.
- Easily extended through [custom-validators](#creating-a-custom-validators).
- Infix [operators](#custom-operators) to provide clean composition syntax, via `Validus.Operators`.
- [Applicative computation expression](#validating-complex-types).
- Excellent for creating [value objects](#value-object) (i.e., cpnstrained primitives).

## Quick Start

A common example of receiving input from an untrusted source `PersonDto` (i.e., HTML form submission), applying validation and producing a result based on success/failure.

```f#
open System
open Validus

type PersonDto =
    { FirstName : string
      LastName  : string
      Email     : string
      Age       : int option
      StartDate : DateTime option }

type Name =
    { First : string
      Last  : string }

type Person =
    { Name      : Name
      Email     : string
      Age       : int option
      StartDate : DateTime }

module Person =
    let ofDto (dto : PersonDto) =
        // Shared validator for first & last name
        let nameValidator =
            Check.String.betweenLen 3 64

        // Composing multiple validators to form complex validation rules,
        // overriding default error message (Note: "Check.WithMessage.String" as
        // opposed to "Check.String")
        let emailValidator =
            let emailPatternValidator =
                let msg = sprintf "Please provide a valid %s"
                Check.WithMessage.String.pattern "[^@]+@[^\.]+\..+" msg

            ValidatorGroup(Check.String.betweenLen 8 512)
                .And(emailPatternValidator)
                .Build()

        // Defining a validator for an option value
        let ageValidator =
            Check.optional (Check.Int.between 1 100)

        // Defining a validator for an option value that is required
        let dateValidator =
            Check.required (Check.DateTime.greaterThan DateTime.Now)

        validate {
          let! first = nameValidator "First name" dto.FirstName
          and! last = nameValidator "Last name" dto.LastName
          and! email = emailValidator "Email address" dto.Email
          and! age = ageValidator "Age" dto.Age
          and! startDate = dateValidator "Start Date" dto.StartDate

          // Construct Person if all validators return Success
          return {
              Name = { First = first; Last = last }
              Email = email
              Age = age
              StartDate = startDate }
        }
```

> Note: This is for demo purposes only, it likely isn't advisable to attempt to validate emails using a regular expression. Instead, use [System.Net.MailAddress](#example-1-email-address-value-object).

And, using the validator:

```fsharp
let dto : PersonDto =
    { FirstName = "John"
      LastName  = "Doe"
      Email     = "john.doe@url.com"
      Age       = Some 63
      StartDate = Some (new DateTime(2058, 1, 1)) }

match validatePersonDto dto with
| Success p -> printfn "%A" p
| Failure e ->
    e
    |> ValidationErrors.toList
    |> Seq.iter (printfn "%s")
```

## Validating Complex Types

Included in Validus is an [applicative computation expression](https://docs.microsoft.com/en-us/dotnet/fsharp/whats-new/fsharp-50#applicative-computation-expressions), which in this case allow validation errors to be accumulated as validators are executed.



```f#
open Validus

type PersonDto =
    { FirstName : string
      LastName  : string
      Age       : int option }

type Name =
    { First : string
      Last  : string }

type Person =
    { Name      : Name
      Age       : int option }

module Person =
    let ofDto (dto : PersonDto) =
        let nameValidator = Check.String.betweenLen 3 64

        let firstNameValidator =
            ValidatorGroup(nameValidator)
                .Then(Check.String.notEquals dto.LastName)
                .Build()

        validate {
          let! first = firstNameValidator "First name" dto.FirstName
          and! last = nameValidator "Last name" dto.LastName
          and! age = Check.optional (Check.Int.between 1 120) "Age" dto.Age

          return {
              Name = { First = first; Last = last }
              Age = age }
        }
```

## Creating A Custom Validator

```f#
open System.Net.Mail
open Validus

let fooValidator =
    let fooRule v = v = "foo"
    let fooMessage = sprintf "%s must be a string that matches 'foo'"
    Validator.create fooMessage fooRule

"bar"
|> fooValidator "Test string"
```

## Combining Validators

Complex validator chains and waterfalls can be created by combining validators together using the `ValidatorGroup` API. Alternatively, a full suite of [operators](#custom-operators) are available, for those who prefer that style of syntax.

```f#
open System.Net.Mail
open Validus

let msg = sprintf "Please provide a valid %s"

let emailPatternValidator =
    Check.WithMessage.String.pattern "[^@]+@[^\.]+\..+" msg

// A custom validator that uses System.Net.Mail to validate email
let mailAddressValidator =
    let rule (x : string) =
        if x = "" then false
        else
            try
                let addr = MailAddress(x)
                if addr.Address = x then true
                else false
            with
            | :? FormatException -> false

    Validator.create msg rule

let emailValidator =
    ValidatorGroup(Check.String.betweenLen 8 512)
        .And(emailPatternValidator)
        .Then(mailAddressValidator) // only executes when prior two steps are `Ok`
        .Build()

"fake@test"
|> emailValidator "Login email"
```

We can use any validator, or combination of validators to validate collections:

```fsharp
let emails = [ "fake@test"; "bob@fsharp.org"; "x" ]

let result =
    emails
    |> List.map (emailValidator "Login email")
```

### Custom Operators

TODO Abel

```fsharp
```

## Value Objects

It is generally a good idea to create [value objects](https://blog.ploeh.dk/2015/01/19/from-primitive-obsession-to-domain-modelling/), sometimes referred to a *value types* or *constrained primitives*, to represent individual data points that are more classified than the primitive types usually used to represent them.

### Example 1: Email Address Value Object

A good example of this is an email address being represented as a `string` literal, as it exists in many programs. This is however a flawed approach in that the domain of an email address is more tightly scoped than a string will allow. For example, `""` or `null` are not valid emails.

To address this, we can create a wrapper type to represent the email address which hides away the implementation details and provides a smart construct to produce the type.

```fsharp
open System.Net.Mail

type Email =
    private { Email : string }

    override x.ToString () = x.Email

    // Note the transformation from string -> Email
    static member Of : Validator<string, Email> = fun field input ->
        let rule (x : string) =
            if x = "" then false
            else
                try
                    let addr = MailAddress(x)
                    if addr.Address = x then true
                    else false
                with
                | :? FormatException -> false

        let message = sprintf "%s must be a valid email address"

        input
        |> Validator.create message rule field
        |> Result.map (fun v -> { Email = v })
```

### Example 2: E164 Formatted Phone Number

```fsharp
type E164 =
    private { E164 : string }

    override x.ToString() = x.E164

    static member Of : Validator<string, E164> = fun field input ->
        let e164Regex = @"^\+[1-9]\d{1,14}$"
        let message = sprintf "%s must be a valid E164 telephone number"

        input
        |> Check.WithMessage.String.pattern e164Regex message field
        |> Result.map (fun v -> { E164 = v })
```

## Built-in Validators

> Note: Validators pre-populated with English-language default error messages reside within the `Check` module.

## [`equals`](https://github.com/pimbrouwers/Validus/blob/cb168960b788ea50914c661fcbba3cf096ec4f3a/src/Validus/Validus.fs#L99)

Applies to: `string, int16, int, int64, decimal, float, DateTime, DateTimeOffset, TimeSpan`

```fsharp
open Validus

// Define a validator which checks if a string equals
// "foo" displaying the standard error message.
let equalsFoo =
  Check.String.equals "foo" "fieldName"

equalsFoo "bar"

// Define a validator which checks if a string equals
// "foo" displaying a custom error message (string -> string).
let equalsFooCustom =
  let msg = sprintf "%s must equal the word 'foo'" "fieldName"
  Check.WithMessage.String.equals "foo" msg

equalsFooCustom "bar"
```

## [`notEquals`](https://github.com/pimbrouwers/Validus/blob/cb168960b788ea50914c661fcbba3cf096ec4f3a/src/Validus/Validus.fs#L103)

Applies to: `string, int16, int, int64, decimal, float, DateTime, DateTimeOffset, TimeSpan`

```fsharp
open Validus

// Define a validator which checks if a string is not
// equal to "foo" displaying the standard error message.
let notEqualsFoo =
  Check.String.notEquals "foo" "fieldName"

notEqualsFoo "bar"

// Define a validator which checks if a string is not
// equal to "foo" displaying a custom error message (string -> string)
let notEqualsFooCustom =
  let msg = sprintf "%s must not equal the word 'foo'" "fieldName"
  Check.WithMessage.String.notEquals "foo" msg

notEqualsFooCustom "bar"
```

## [`between`](https://github.com/pimbrouwers/Validus/blob/cb168960b788ea50914c661fcbba3cf096ec4f3a/src/Validus/Validus.fs#L110) (inclusive)

Applies to: `int16, int, int64, decimal, float, DateTime, DateTimeOffset, TimeSpan`

```fsharp
open Validus

// Define a validator which checks if an int is between
// 1 and 100 (inclusive) displaying the standard error message.
let between1and100 =
  Check.Int.between 1 100 "fieldName"

between1and100 12 // Result<int, ValidationErrors>

// Define a validator which checks if an int is between
// 1 and 100 (inclusive) displaying a custom error message.
let between1and100Custom =
  let msg = sprintf "%s must be between 1 and 100" "fieldName"
  Check.WithMessage.Int.between 1 100 msg

between1and100Custom 12 // Result<int, ValidationErrors>
```

## [`greaterThan`](https://github.com/pimbrouwers/Validus/blob/cb168960b788ea50914c661fcbba3cf096ec4f3a/src/Validus/Validus.fs#L114)

Applies to: `int16, int, int64, decimal, float, DateTime, DateTimeOffset, TimeSpan`

```fsharp
open Validus

// Define a validator which checks if an int is greater than
// 100 displaying the standard error message.
let greaterThan100 =
  Check.Int.greaterThan 100 "fieldName"

greaterThan100 12 // Result<int, ValidationErrors>

// Define a validator which checks if an int is greater than
// 100 displaying a custom error message.
let greaterThan100Custom =
  let msg = sprintf "%s must be greater than 100" "fieldName"
  Check.WithMessage.Int.greaterThan 100 msg

greaterThan100Custom 12 // Result<int, ValidationErrors>
```

## [`lessThan`](https://github.com/pimbrouwers/Validus/blob/cb168960b788ea50914c661fcbba3cf096ec4f3a/src/Validus/Validus.fs#L118)

Applies to: `int16, int, int64, decimal, float, DateTime, DateTimeOffset, TimeSpan`

```fsharp
open Validus

// Define a validator which checks if an int is less than
// 100 displaying the standard error message.
let lessThan100 =
  Check.Int.lessThan 100 "fieldName"

lessThan100 12 // Result<int, ValidationErrors>

// Define a validator which checks if an int is less than
// 100 displaying a custom error message.
let lessThan100Custom =
  let msg = sprintf "%s must be less than 100" "fieldName"
  Check.WithMessage.Int.lessThan 100 msg

lessThan100Custom 12 // Result<int, ValidationErrors>
```

## [`betweenLen`](https://github.com/pimbrouwers/Validus/blob/cb168960b788ea50914c661fcbba3cf096ec4f3a/src/Validus/Validus.fs#L126)

Applies to: `string`

```fsharp
open Validus

// Define a validator which checks if a string is between
// 1 and 100 chars displaying the standard error message.
let between1and100Chars =
  Check.String.betweenLen 1 100 "fieldName"

between1and100Chars "validus"

// Define a validator which checks if a string is between
// 1 and 100 chars displaying a custom error message.
let between1and100CharsCustom =
  let msg = sprintf "%s must be between 1 and 100 chars" "fieldName"
  Check.WithMessage.String.betweenLen 1 100 msg

between1and100CharsCustom "validus"
```

## [`equalsLen`](https://github.com/pimbrouwers/Validus/blob/e555cc01f41f2d717ecec32fcb46616dca7243e8/src/Validus/Validus.fs#L219)

Applies to: `string`

```fsharp
open Validus

// Define a validator which checks if a string is equals to
// 100 chars displaying the standard error message.
let equals100Chars =
  Check.String.equalsLen 100 "fieldName"

equals100Chars "validus"

// Define a validator which checks if a string is equals to
// 100 chars displaying a custom error message.
let equals100CharsCustom =
  let msg = sprintf "%s must be 100 chars" "fieldName"
  Check.WithMessage.String.equalsLen 100 msg

equals100CharsCustom "validus"
```

## [`greaterThanLen`](https://github.com/pimbrouwers/Validus/blob/cb168960b788ea50914c661fcbba3cf096ec4f3a/src/Validus/Validus.fs#L136)

Applies to: `string`

```fsharp
open Validus

// Define a validator which checks if a string is greater than
// 100 chars displaying the standard error message.
let greaterThan100Chars =
  Check.String.greaterThanLen 100 "fieldName"

greaterThan100Chars "validus"

// Define a validator which checks if a string is greater than
// 100 chars displaying a custom error message.
let greaterThan100CharsCustom =
  let msg = sprintf "%s must be greater than 100 chars" "fieldName"
  Check.WithMessage.String.greaterThanLen 100 msg

greaterThan100CharsCustom "validus"
```

## [`lessThanLen`](https://github.com/pimbrouwers/Validus/blob/cb168960b788ea50914c661fcbba3cf096ec4f3a/src/Validus/Validus.fs#L141)

Applies to: `string`

```fsharp
open Validus

// Define a validator which checks if a string is less tha
// 100 chars displaying the standard error message.
let lessThan100Chars =
  Check.String.lessThanLen 100 "fieldName"

lessThan100Chars "validus"

// Define a validator which checks if a string is less tha
// 100 chars displaying a custom error message.
let lessThan100CharsCustom =
  let msg = sprintf "%s must be less than 100 chars" "fieldName"
  Check.WithMessage.String.lessThanLen 100 msg

lessThan100CharsCustom "validus"
```

## [`empty`](https://github.com/pimbrouwers/Validus/blob/cb168960b788ea50914c661fcbba3cf096ec4f3a/src/Validus/Validus.fs#L131)

Applies to: `string`

```fsharp
open Validus

// Define a validator which checks if a string is empty
// displaying the standard error message.
let stringIsEmpty =
  Check.String.empty "fieldName"

stringIsEmpty "validus"

// Define a validator which checks if a string is empty
// displaying a custom error message.
let stringIsEmptyCustom =
  let msg = sprintf "%s must be empty" "fieldName"
  Check.WithMessage.String.empty msg

stringIsEmptyCustom "validus"
```

## [`notEmpty`](https://github.com/pimbrouwers/Validus/blob/cb168960b788ea50914c661fcbba3cf096ec4f3a/src/Validus/Validus.fs#L146)

Applies to: `string`

```fsharp
open Validus

// Define a validator which checks if a string is not empty
// displaying the standard error message.
let stringIsNotEmpty =
  Check.String.notEmpty "fieldName"

stringIsNotEmpty "validus"

// Define a validator which checks if a string is not empty
// displaying a custom error message.
let stringIsNotEmptyCustom =
  let msg = sprintf "%s must not be empty" "fieldName"
  Check.WithMessage.String.notEmpty msg

stringIsNotEmptyCustom "validus"
```

## [`pattern`](https://github.com/pimbrouwers/Validus/blob/cb168960b788ea50914c661fcbba3cf096ec4f3a/src/Validus/Validus.fs#L151) (Regular Expressions)

Applies to: `string`

```fsharp
open Validus

// Define a validator which checks if a string matches the
// provided regex displaying the standard error message.
let stringIsChars =
  Check.String.pattern "[a-z]+" "fieldName"

stringIsChars "validus"

// Define a validator which checks if a string matches the
// provided regex displaying a custom error message.
let stringIsCharsCustom =
  let msg = sprintf "%s must follow the pattern [a-z]" "fieldName"
  Check.WithMessage.String.pattern "[a-z]" msg

stringIsCharsCustom "validus"
```

## Find a bug?

There's an [issue](https://github.com/pimbrouwers/Validus/issues) for that.

## License

Built with ♥ by [Pim Brouwers](https://github.com/pimbrouwers) in Toronto, ON. Licensed under [Apache License 2.0](https://github.com/pimbrouwers/Validus/blob/master/LICENSE).
