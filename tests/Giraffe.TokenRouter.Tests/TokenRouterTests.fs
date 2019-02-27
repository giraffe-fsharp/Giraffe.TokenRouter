module Giraffe.Tests.TokenRouterTests

open System
open System.Collections.Generic
open System.IO
open System.Linq
open System.Xml.Linq
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open Xunit
open Xunit.Abstractions
open NSubstitute
open Newtonsoft.Json
open Giraffe
open Giraffe.Serialization
open Giraffe.GiraffeViewEngine
open Giraffe.TokenRouter
open FSharp.Control.Tasks.V2.ContextInsensitive

// ---------------------------------
// XmlAssert
// ---------------------------------

module XmlAssert =
    let rec normalize (element : XElement) =
        if element.HasElements then
            XElement(
                element.Name,
                element.Attributes()
                    .Where(fun a -> a.Name.Namespace = XNamespace.Xmlns)
                    .OrderBy(fun a -> a.Name.ToString()),
                element.Elements()
                    .OrderBy(fun a -> a.Name.ToString())
                    .Select(fun e -> normalize(e))
            )
        elif element.IsEmpty then
            XElement(
                element.Name,
                element.Attributes()
                    .OrderBy(fun a -> a.Name.ToString())
              )
         else
            XElement(
                element.Name,
                element.Attributes()
                    .OrderBy(fun a -> a.Name.ToString()), element.Value
               )

    let equals expectedXml actualXml =
        let expectedXElement = XElement.Parse expectedXml |> normalize
        let actualXElement = XElement.Parse actualXml |> normalize
        Assert.Equal(expectedXElement.ToString(), actualXElement.ToString())

// ---------------------------------
// Helper functions
// ---------------------------------

let getStatusCode (ctx : HttpContext) =
    ctx.Response.StatusCode

let getBody (ctx : HttpContext) =
    ctx.Response.Body.Position <- 0L
    use reader = new StreamReader(ctx.Response.Body, Encoding.UTF8)
    reader.ReadToEnd()

let getContentType (response : HttpResponse) =
    response.Headers.["Content-Type"].[0]

let assertFail msg = Assert.True(false, msg)

let assertFailf format args =
    let msg = sprintf format args
    Assert.True(false, msg)

let notFound = setStatusCode 404 >=> text "Not found"
let next : HttpFunc = Some >> Task.FromResult

let mockJson (ctx : HttpContext) (settings : JsonSerializerSettings option) =
    let jsonSettings =
        defaultArg settings NewtonsoftJsonSerializer.DefaultSettings
    ctx.RequestServices
       .GetService(typeof<IJsonSerializer>)
       .Returns(NewtonsoftJsonSerializer(jsonSettings))
    |> ignore

let mockXml (ctx : HttpContext) =
    ctx.RequestServices
       .GetService(typeof<IXmlSerializer>)
       .Returns(DefaultXmlSerializer(DefaultXmlSerializer.DefaultSettings))
    |> ignore

let mockNegotiation (ctx : HttpContext) =
    ctx.RequestServices
       .GetService(typeof<INegotiationConfig>)
       .Returns(DefaultNegotiationConfig())
    |> ignore

// ---------------------------------
// Test Types
// ---------------------------------

type Dummy =
    {
        Foo : string
        Bar : string
        Age : int
    }

[<CLIMutable>]
type Person =
    {
        FirstName : string
        LastName  : string
        BirthDate : DateTime
        Height    : float
        Piercings : string[]
    }
    override this.ToString() =
        let nl = Environment.NewLine
        sprintf "First name: %s%sLast name: %s%sBirth date: %s%sHeight: %.2f%sPiercings: %A"
            this.FirstName nl
            this.LastName nl
            (this.BirthDate.ToString("yyyy-MM-dd")) nl
            this.Height nl
            this.Piercings

// ---------------------------------
// Tests
// ---------------------------------

[<Fact>]
let ``GET "/" returns "Hello World"`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route "/" => text "Hello World"
                route "/foo" => text "bar" ]]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Hello World"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``GET "/foo" returns "bar"`` () =
    let ctx = Substitute.For<HttpContext>()
    let notFound = setStatusCode 404 >=> text "Not found"
    let app =
        router notFound [
            GET [
                route "/"    => text "Hello World"
                route "/foo" => text "bar"]
            ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "bar"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``GET "/FOO" returns 404 "Not found"`` () =
    let ctx = Substitute.For<HttpContext>()
    let notFound = setStatusCode 404 >=> text "Not found"
    let app =
        router notFound [
            GET [
                route "/"    => text "Hello World"
                route "/foo" => text "bar"
            ]]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/FOO")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Not found"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            Assert.Equal(expected, body)
            Assert.Equal(404, ctx.Response.StatusCode)
    }

[<Fact>]
let ``GET "/json" returns json object`` () =
    let ctx = Substitute.For<HttpContext>()
    mockJson ctx None
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
                route "/json" => json { Foo = "john"; Bar = "doe"; Age = 30 }
            ]]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/json")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "{\"foo\":\"john\",\"bar\":\"doe\",\"age\":30}"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``POST "/post/1" returns "1"`` () =
    let ctx = Substitute.For<HttpContext>()
    let notFound = setStatusCode 404 >=> text "Not found"
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
            ]
            POST [
                route "/post/1" => text "1"
                route "/post/2" => text "2"
            ]
        ]

    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/post/1")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "1"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``POST "/post/2" returns "2"`` () =
    let ctx = Substitute.For<HttpContext>()
    let notFound = setStatusCode 404 >=> text "Not found"
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
                ]
            POST [
                route "/post/1" => text "1"
                route "/post/2" => text "2"
            ]
        ]

    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/post/2")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "2"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``PUT "/post/2" returns 404 "Not found"`` () =
    let ctx = Substitute.For<HttpContext>()
    let notFound = setStatusCode 404 >=> text "Not found"
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar" ]
            POST [
                route "/post/1" => text "1"
                route "/post/2" => text "2" ]
            ]

    ctx.Request.Method.ReturnsForAnyArgs "PUT" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/post/2")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Not found"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            Assert.Equal(expected, body)
            Assert.Equal(404, ctx.Response.StatusCode)
    }

[<Fact>]
let ``POST "/text" with supported Accept header returns "good"`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar" ]
            POST [
                route "/text"   (mustAccept [ "text/plain" ] >=> text "text")
                route "/json"   (mustAccept [ "application/json" ] >=> json "json")
                route "/either" (mustAccept [ "text/plain"; "application/json" ] >=> text "either") ]
            ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("text/plain"))
    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/text")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "text"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            Assert.Equal(expected, body)
            Assert.Equal("text/plain; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``POST "/json" with supported Accept header returns "json"`` () =
    let ctx = Substitute.For<HttpContext>()
    mockJson ctx None
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar" ]
            POST [
                route "/text"   ( mustAccept [ "text/plain" ] >=> text "text")
                route "/json"   ( mustAccept [ "application/json" ] >=> json "json")
                route "/either" ( mustAccept [ "text/plain"; "application/json" ] >=> text "either") ]
            ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("application/json"))
    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/json")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "\"json\""

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            Assert.Equal(expected, body)
            Assert.Equal("application/json; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``POST "/either" with supported Accept header returns "either"`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar" ]
            POST [
                route "/text"   ( mustAccept [ "text/plain" ] >=> text "text" )
                route "/json"   ( mustAccept [ "application/json" ] >=> json "json" )
                route "/either" ( mustAccept [ "text/plain"; "application/json" ] >=> text "either" ) ]
            ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("application/json"))
    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/either")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "either"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            Assert.Equal(expected, body)
            Assert.Equal("text/plain; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``POST "/either" with unsupported Accept header returns 404 "Not found"`` () =
    let ctx = Substitute.For<HttpContext>()
    let notFound = setStatusCode 404 >=> text "Not found"
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar" ]
            POST [
                route "/text"   ( mustAccept [ "text/plain" ] >=> text "text" )
                route "/json"   ( mustAccept [ "application/json" ] >=> json "json" )
                route "/either" ( mustAccept [ "text/plain"; "application/json" ] >=> text "either" ) ]
        ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("application/xml"))
    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/either")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Not found"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            Assert.Equal(expected, body)
            Assert.Equal(404, ctx.Response.StatusCode)
    }

// [<Fact>]
// let ``GET "/JSON" returns "BaR"`` () =
//     let ctx = Substitute.For<HttpContext>()
//     mockJson ctx None
//     let app =
//         router notFound [
//             GET [
//                 route   "/"       => text "Hello World"
//                 route   "/foo"    => text "bar"
//                 route   "/json"   => json { Foo = "john"; Bar = "doe"; Age = 30 }
//                 routeCi "/json"   => text "BaR" ]
//         ]

//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/JSON")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     let expected = "BaR"

//     task {
//         let! result = app next ctx

//         match result with
//         | None     -> assertFailf "Result was expected to be %s" expected
//         | Some ctx -> Assert.Equal(expected, getBody ctx)
//     }

[<Fact>]
let ``GET "/foo/blah blah/bar" returns "blah blah"`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route   "/"       => text "Hello World"
                route   "/foo"    => text "bar"
                routef "/foo/%s/bar" text
                routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
            ]
        ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo/blah blah/bar")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "blah blah"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``GET "/foo/johndoe/59" returns "Name: johndoe, Age: 59"`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route   "/"       => text "Hello World"
                route   "/foo"    => text "bar"
                routef "/foo/%s/bar" text
                routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
            ]
        ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo/johndoe/59")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Name: johndoe, Age: 59"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``GET "/foo/johndoe/FE9CFE19-35D4-4EDC-9A95-5D38C4D579BD" returns "Name: johndoe, Id: FE9CFE19-35D4-4EDC-9A95-5D38C4D579BD"`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route   "/"       => text "Hello World"
                route   "/foo"    => text "bar"
                routef "/foo/%s/bar" text
                routef "/foo/%s/%O" (fun (name, id: Guid) -> text (sprintf "Name: %s, Id: %O" name id))
            ]
        ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo/johndoe/FE9CFE19-35D4-4EDC-9A95-5D38C4D579BD")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Name: johndoe, Id: FE9CFE19-35D4-4EDC-9A95-5D38C4D579BD"

    task {
        let! result = app next ctx
        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx, true)
    }

[<Fact>]
let ``GET "/foo/johndoe/FE9CFE1935D44EDC9A955D38C4D579BD" returns "Name: johndoe, Id: FE9CFE19-35D4-4EDC-9A95-5D38C4D579BD"`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route   "/"       => text "Hello World"
                route   "/foo"    => text "bar"
                routef "/foo/%s/bar" text
                routef "/foo/%s/%O" (fun (name, id: Guid) -> text (sprintf "Name: %s, Id: %O" name id))
            ]
        ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo/johndoe/FE9CFE1935D44EDC9A955D38C4D579BD")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Name: johndoe, Id: FE9CFE19-35D4-4EDC-9A95-5D38C4D579BD"

    task {
        let! result = app next ctx
        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx, true)
    }

[<Fact>]
let ``GET "/foo/johndoe/Gf6c_tQ13E6alV04xNV5vQ" returns "Name: johndoe, Id: FE9CFE19-35D4-4EDC-9A95-5D38C4D579BD"`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route   "/"       => text "Hello World"
                route   "/foo"    => text "bar"
                routef "/foo/%s/bar" text
                routef "/foo/%s/%O" (fun (name, id: Guid) -> text (sprintf "Name: %s, Id: %O" name id))
            ]
        ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo/johndoe/Gf6c_tQ13E6alV04xNV5vQ")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Name: johndoe, Id: FE9CFE19-35D4-4EDC-9A95-5D38C4D579BD"

    task {
        let! result = app next ctx
        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx, true)
    }

// [<Fact>]
// let ``POST "/POsT/1" returns "1"`` () =
//     let ctx = Substitute.For<HttpContext>()
//     let app =
//         choose [
//             router notFound [ GET [
//                 route "/" => text "Hello World" ]
//             router notFound [ POST [
//                 route    "/post/1" => text "1"
//                 routeCif "/post/%i" json ]
//             ]

//     ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/POsT/1")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     let expected = "1"

//     task {
//         let! result = app next ctx

//         match result with
//         | None     -> assertFailf "Result was expected to be %s" expected
//         | Some ctx -> Assert.Equal(expected, getBody ctx)
//     }

// [<Fact>]
// let ``POST "/POsT/523" returns "523"`` () =
//     let ctx = Substitute.For<HttpContext>()
//     let app =
//         router notFound [
//             router notFound [ GET [
//                 route "/" => text "Hello World" ]
//             router notFound [ POST [
//                 route    "/post/1" => text "1"
//                 routeCif "/post/%i" json ]
//             ]

//     ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/POsT/523")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     let expected = "523"

//     task {
//         let! result = app next ctx

//         match result with
//         | None     -> assertFailf "Result was expected to be %s" expected
//         | Some ctx -> Assert.Equal(expected, getBody ctx)
//     }

[<Fact>]
let ``Sub route with empty route`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route "/"    => text "Hello World"
                route "/foo" => text "bar"
                subRoute "/api" [
                        route ""       => text "api root"
                        route "/admin" => text "admin"
                        route "/users" => text "users" ]
                route "/api/test" => text "test"
            ]
        ]

    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/api")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "api root"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``Sub route with non empty route`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route "/"    => text "Hello World"
                route "/foo" => text "bar"
                subRoute "/api" [
                        route ""       => text "api root"
                        route "/admin" => text "admin"
                        route "/users" => text "users" ]
                route "/api/test" => text "test"
            ]
        ]

    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/users")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "users"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``Route after sub route with same beginning of path`` () =

    task {
        let ctx = Substitute.For<HttpContext>()

        let app =
            router notFound [
                GET [
                    route "/"    => text "Hello World"
                    route "/foo" => text "bar"
                    subRoute "/api" [
                            route ""       => text "api root"
                            route "/admin" => text "admin"
                            route "/users" => text "users" ]
                    route "/api/test" => text "test"
                ]
            ]

        ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
        ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
        ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/test")) |> ignore
        ctx.Response.Body <- new MemoryStream()
        let expected = "test"

        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``Nested sub routes`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route "/"    => text "Hello World"
                route "/foo" => text "bar"
                subRoute "/api" [
                        route ""       => text "api root"
                        route "/admin" => text "admin"
                        route "/users" => text "users"
                        subRoute "/v2" [
                                route ""       => text "api root v2"
                                route "/admin" => text "admin v2"
                                route "/users" => text "users v2"
                            ]
                    ]
                route "/api/test" => text "test"
            ]
        ]

    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/v2/users")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "users v2"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``Route after nested sub routes with same beginning of path`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route "/"    => text "Hello World"
                route "/foo" => text "bar"
                subRoute "/api" [
                        route ""       => text "api root"
                        route "/admin" => text "admin"
                        route "/users" => text "users"
                        subRoute "/v2" [
                                route ""       => text "api root v2"
                                route "/admin" => text "admin v2"
                                route "/users" => text "users v2"
                            ]
                        route "/yada" => text "yada"
                    ]
                route "/api/test"   => text "test"
                route "/api/v2/else" => text "else"
            ]
        ]

    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/v2/else")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "else"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``Multiple nested sub routes`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route "/"    => text "Hello World"
                route "/foo" => text "bar"
                subRoute "/api"  [
                        route "/users" => text "users"
                        subRoute "/v2" [
                            route "/admin" => text "admin v2"
                            route "/users" => text "users v2" ]
                        subRoute "/v2" [
                            route "/admin2" => text "correct admin2" ]
                    ]
                route "/api/test"   => text "test"
                route "/api/v2/else" => text "else"
            ]
        ]

    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/v2/admin2")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "correct admin2"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

//todo : fix - failing
[<Fact>]
let ``GET "/api/foo/bar/yadayada" returns "yadayada"`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route "/"    => text "Hello World"
                route "/foo" => text "bar"
                subRoute "/api" [
                        route  "" => text "api root"
                        routef "/foo/bar/%s" text ]
                route "/api/test" => text "test"
            ]
        ]

    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/foo/bar/yadayada")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "yadayada"

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFailf "Result was expected to be %s" expected
        | Some ctx -> Assert.Equal(expected, getBody ctx)
    }

[<Fact>]
let ``GET "/person" returns rendered HTML view`` () =
    let ctx = Substitute.For<HttpContext>()
    let personView model =
        html [] [
            head [] [
                title [] [ encodedText "Html Node" ]
            ]
            body [] [
                p [] [ sprintf "%s %s is %i years old." model.Foo model.Bar model.Age |> encodedText ]
            ]
        ]

    let johnDoe = { Foo = "John"; Bar = "Doe"; Age = 30 }

    let app =
        router notFound [
            GET [
                route "/"          => text "Hello World"
                route "/person"    => (personView johnDoe |> htmlView) ]
            POST [
                route "/post/1"    => text "1" ]
        ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/person")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "<!DOCTYPE html><html><head><title>Html Node</title></head><body><p>John Doe is 30 years old.</p></body></html>"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = (getBody ctx).Replace(Environment.NewLine, String.Empty)
            Assert.Equal(expected, body)
            Assert.Equal("text/html; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``Get "/auto" with Accept header of "application/json" returns JSON object`` () =
    let johnDoe =
        {
            FirstName = "John"
            LastName  = "Doe"
            BirthDate = DateTime(1990, 7, 12)
            Height    = 1.85
            Piercings = [| "left ear"; "nose" |]
        }

    let ctx = Substitute.For<HttpContext>()
    mockJson ctx None
    mockNegotiation ctx
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
                route "/auto" => negotiate johnDoe
            ]
        ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("application/json"))
    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/auto")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()

    let expected = "{\"firstName\":\"John\",\"lastName\":\"Doe\",\"birthDate\":\"1990-07-12T00:00:00\",\"height\":1.85,\"piercings\":[\"left ear\",\"nose\"]}"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            Assert.Equal(expected, body)
            Assert.Equal("application/json; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``Get "/auto" with Accept header of "application/xml; q=0.9, application/json" returns JSON object`` () =
    let johnDoe =
        {
            FirstName = "John"
            LastName  = "Doe"
            BirthDate = DateTime(1990, 7, 12)
            Height    = 1.85
            Piercings = [| "left ear"; "nose" |]
        }

    let ctx = Substitute.For<HttpContext>()
    mockJson ctx None
    mockNegotiation ctx
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
                route "/auto" => negotiate johnDoe
            ]
        ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("application/xml; q=0.9, application/json"))
    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/auto")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()

    let expected = "{\"firstName\":\"John\",\"lastName\":\"Doe\",\"birthDate\":\"1990-07-12T00:00:00\",\"height\":1.85,\"piercings\":[\"left ear\",\"nose\"]}"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            Assert.Equal(expected, body)
            Assert.Equal("application/json; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``Get "/auto" with Accept header of "application/xml" returns XML object`` () =
    let johnDoe =
        {
            FirstName = "John"
            LastName  = "Doe"
            BirthDate = DateTime(1990, 7, 12)
            Height    = 1.85
            Piercings = [| "ear"; "nose" |]
        }

    let ctx = Substitute.For<HttpContext>()
    mockXml ctx
    mockNegotiation ctx
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
                route "/auto" => negotiate johnDoe
            ]
        ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("application/xml"))
    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/auto")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()

    let expected = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Person xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <FirstName>John</FirstName>
  <LastName>Doe</LastName>
  <BirthDate>1990-07-12T00:00:00</BirthDate>
  <Height>1.85</Height>
  <Piercings>
    <string>ear</string>
    <string>nose</string>
  </Piercings>
</Person>"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            XmlAssert.equals expected body
            Assert.Equal("application/xml; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``Get "/auto" with Accept header of "application/xml, application/json" returns XML object`` () =
    let johnDoe =
        {
            FirstName = "John"
            LastName  = "Doe"
            BirthDate = DateTime(1990, 7, 12)
            Height    = 1.85
            Piercings = [| "ear"; "nose" |]
        }

    let ctx = Substitute.For<HttpContext>()
    mockXml ctx
    mockNegotiation ctx
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
                route "/auto" => negotiate johnDoe
            ]
        ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("application/xml, application/json"))
    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/auto")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()

    let expected = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Person xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <FirstName>John</FirstName>
  <LastName>Doe</LastName>
  <BirthDate>1990-07-12T00:00:00</BirthDate>
  <Height>1.85</Height>
  <Piercings>
    <string>ear</string>
    <string>nose</string>
  </Piercings>
</Person>"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            XmlAssert.equals expected body
            Assert.Equal("application/xml; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``Get "/auto" with Accept header of "application/json, application/xml" returns JSON object`` () =
    let johnDoe =
        {
            FirstName = "John"
            LastName  = "Doe"
            BirthDate = DateTime(1990, 7, 12)
            Height    = 1.85
            Piercings = [| "ear"; "nose" |]
        }

    let ctx = Substitute.For<HttpContext>()
    mockJson ctx None
    mockNegotiation ctx
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
                route "/auto" => negotiate johnDoe
            ]
        ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("application/json, application/xml"))
    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/auto")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()

    let expected = "{\"firstName\":\"John\",\"lastName\":\"Doe\",\"birthDate\":\"1990-07-12T00:00:00\",\"height\":1.85,\"piercings\":[\"ear\",\"nose\"]}"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            Assert.Equal(expected, body)
            Assert.Equal("application/json; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``Get "/auto" with Accept header of "application/json; q=0.5, application/xml" returns XML object`` () =
    let johnDoe =
        {
            FirstName = "John"
            LastName  = "Doe"
            BirthDate = DateTime(1990, 7, 12)
            Height    = 1.85
            Piercings = [| "ear"; "nose" |]
        }

    let ctx = Substitute.For<HttpContext>()
    mockXml ctx
    mockNegotiation ctx
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
                route "/auto" => negotiate johnDoe
            ]
        ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("application/json; q=0.5, application/xml"))
    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/auto")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()

    let expected = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Person xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <FirstName>John</FirstName>
  <LastName>Doe</LastName>
  <BirthDate>1990-07-12T00:00:00</BirthDate>
  <Height>1.85</Height>
  <Piercings>
    <string>ear</string>
    <string>nose</string>
  </Piercings>
</Person>"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            XmlAssert.equals expected body
            Assert.Equal("application/xml; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``Get "/auto" with Accept header of "application/json; q=0.5, application/xml; q=0.6" returns XML object`` () =
    let johnDoe =
        {
            FirstName = "John"
            LastName  = "Doe"
            BirthDate = DateTime(1990, 7, 12)
            Height    = 1.85
            Piercings = [| "ear"; "nose" |]
        }

    let ctx = Substitute.For<HttpContext>()
    mockXml ctx
    mockNegotiation ctx
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
                route "/auto" => negotiate johnDoe
            ]
        ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("application/json; q=0.5, application/xml; q=0.6"))
    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/auto")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()

    let expected = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Person xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <FirstName>John</FirstName>
  <LastName>Doe</LastName>
  <BirthDate>1990-07-12T00:00:00</BirthDate>
  <Height>1.85</Height>
  <Piercings>
    <string>ear</string>
    <string>nose</string>
  </Piercings>
</Person>"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            XmlAssert.equals expected body
            Assert.Equal("application/xml; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``Get "/auto" with Accept header of "text/plain; q=0.7, application/xml; q=0.6" returns text object`` () =
    let johnDoe =
        {
            FirstName = "John"
            LastName  = "Doe"
            BirthDate = DateTime(1990, 7, 12)
            Height    = 1.85
            Piercings = [| "ear"; "nose" |]
        }

    let ctx = Substitute.For<HttpContext>()
    mockNegotiation ctx
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
                route "/auto" => negotiate johnDoe
            ]
        ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("text/plain; q=0.7, application/xml; q=0.6"))
    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/auto")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()

    let expected = @"First name: John
Last name: Doe
Birth date: 1990-07-12
Height: 1.85
Piercings: [|""ear""; ""nose""|]"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            Assert.Equal(expected, body)
            Assert.Equal("text/plain; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``Get "/auto" with Accept header of "text/html" returns a 406 response`` () =
    let johnDoe =
        {
            FirstName = "John"
            LastName  = "Doe"
            BirthDate = DateTime(1990, 7, 12)
            Height    = 1.85
            Piercings = [| "ear"; "nose" |]
        }

    let ctx = Substitute.For<HttpContext>()
    mockNegotiation ctx
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
                route "/auto" => negotiate johnDoe
            ]
        ]

    let headers = HeaderDictionary()
    headers.Add("Accept", StringValues("text/html"))
    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/auto")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()

    let expected = "text/html is unacceptable by the server."

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            Assert.Equal(406, getStatusCode ctx)
            Assert.Equal(expected, body)
            Assert.Equal("text/plain; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``Get "/auto" without an Accept header returns a JSON object`` () =
    let johnDoe =
        {
            FirstName = "John"
            LastName  = "Doe"
            BirthDate = DateTime(1990, 7, 12)
            Height    = 1.85
            Piercings = [| "ear"; "nose" |]
        }

    let ctx = Substitute.For<HttpContext>()
    mockJson ctx None
    mockNegotiation ctx
    let app =
        router notFound [
            GET [
                route "/"     => text "Hello World"
                route "/foo"  => text "bar"
                route "/auto" => negotiate johnDoe
            ]
        ]

    let headers = HeaderDictionary()
    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/auto")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()

    let expected = "{\"firstName\":\"John\",\"lastName\":\"Doe\",\"birthDate\":\"1990-07-12T00:00:00\",\"height\":1.85,\"piercings\":[\"ear\",\"nose\"]}"

    task {
        let! result = app next ctx

        match result with
        | None -> assertFailf "Result was expected to be %s" expected
        | Some ctx ->
            let body = getBody ctx
            Assert.Equal(expected, body)
            Assert.Equal("application/json; charset=utf-8", ctx.Response |> getContentType)
    }

[<Fact>]
let ``Warbler function should execute inner function each time`` () =
    let ctx = Substitute.For<HttpContext>()
    let inner() = Guid.NewGuid().ToString()
    let app =
        router notFound [
            GET [
                route "/foo"  => text (inner())
                route "/foo2" => warbler (fun _ -> text (inner())) ]]
        <| next

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo")) |> ignore
    ctx.Response.Body <- new MemoryStream()

    task {
        let! res1 = app ctx
        let result1 = getBody res1.Value

        ctx.Response.Body <- new MemoryStream()

        let! res2 = app ctx
        let result2 = getBody res2.Value

        Assert.Equal(result1, result2)

        ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo2")) |> ignore
        ctx.Response.Body <- new MemoryStream()

        let! res3 = app ctx
        let result3 = getBody res3.Value

        ctx.Response.Body <- new MemoryStream()

        let! res4 = app ctx
        let result4 = getBody res4.Value

        Assert.False(result3.Equals result4)
    }

[<Fact>]
let ``GET "/redirect" redirect to "/" `` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            GET [
                route "/"         => text "Hello World"
                route "/redirect" => redirectTo false "/"
            ]
        ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/redirect")) |> ignore

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFail "It was expected that the request would be redirected"
        | Some ctx -> ctx.Response.Received().Redirect("/", false)

    }

[<Fact>]
let ``POST "/redirect" redirect to "/" `` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            POST [
                route "/"         => text "Hello World"
                route "/redirect" => redirectTo true "/"
            ]
        ]

    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/redirect")) |> ignore

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFail "It was expected that the request would be redirected"
        | Some ctx -> ctx.Response.Received().Redirect("/", true)
    }

[<Fact>]
let ``HEAD "/foo/johndoe" returns a 204 response`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            HEAD [
                route  "/"         => text "Hello World"
                route  "/redirect" => redirectTo true "/"
                routef "/foo/%s"      (fun _ -> Successful.NO_CONTENT)
            ]
        ]

    ctx.Request.Method.ReturnsForAnyArgs "HEAD" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo/johndoe")) |> ignore

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFail "The request should have matched the /foo/%s route"
        | Some ctx -> Assert.Equal(204, ctx.Response.StatusCode)
    }

[<Fact>]
let ``OPTIONS "/foo/johndoe" returns a 200 response`` () =
    let ctx = Substitute.For<HttpContext>()
    let app =
        router notFound [
            OPTIONS [
                route  "/"         => text "Hello World"
                route  "/redirect" => redirectTo true "/"
                routef "/foo/%s"      (fun _ -> setStatusCode 200 >=> text "howdy")
            ]
        ]

    ctx.Request.Method.ReturnsForAnyArgs "OPTIONS" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo/johndoe")) |> ignore
    ctx.Response.Body <- new MemoryStream()

    task {
        let! result = app next ctx

        match result with
        | None     -> assertFail "The request should have matched the /foo/%s route"
        | Some ctx -> Assert.Equal(200, ctx.Response.StatusCode)
    }

type DebugTests(output:ITestOutputHelper) =

    [<Fact>]
    member __.``Pre-route method filtering`` () =
        let ctx = Substitute.For<HttpContext>()
        let notFound = (setStatusCode 404 >=> text "Not Found")
        let app =
            routerDbg output.WriteLine notFound [
                GET [
                    route  "/index"          => text "index page" ]
                POST [
                    subRoute "/api" [
                        route "/newpassword" => text "newpassword" ]
                ]
            ]

        let expected = "newpassword"
        ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
        ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/newpassword")) |> ignore
        ctx.Response.Body <- new MemoryStream()

        task {
            let! result = app (Some >> Task.FromResult) ctx

            match result with
            | None     -> assertFail "No Route matched"
            | Some ctx ->
                let body = getBody ctx
                Assert.Equal(expected, body)
        }

    [<Fact>]
    member __.``Test routePorts function`` () =
        let ctx = Substitute.For<HttpContext>()
        let notFound = (setStatusCode 404 >=> text "Not Found")
        let app1 =
            routerDbg output.WriteLine notFound [
                GET [
                    route  "/index1"          => text "index page1" ]
                POST [
                    subRoute "/api1" [
                        route "/newpassword1" => text "newpassword1" ]
                ]
            ]
        let app2 =
            routerDbg output.WriteLine notFound [
                GET [
                    route  "/index2"          => text "index page2" ]
                POST [
                    subRoute "/api2" [
                        route "/newpassword2" => text "newpassword2" ]
                ]
            ]

        let app = routePorts [ (9001, app1); (9002, app2) ]

        let expected = "newpassword2"
        ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
        ctx.Request.Path.ReturnsForAnyArgs (PathString("/api2/newpassword2")) |> ignore
        ctx.Request.Host.ReturnsForAnyArgs (HostString("", 9002)) |> ignore
        ctx.Response.Body <- new MemoryStream()

        task {
            let! result = app (Some >> Task.FromResult) ctx

            match result with
            | None     -> assertFail "No Route matched"
            | Some ctx ->
                let body = getBody ctx
                Assert.Equal(expected, body)
        }