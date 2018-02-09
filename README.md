# Giraffe.TokenRouter

![Giraffe](https://raw.githubusercontent.com/giraffe-fsharp/Giraffe/master/giraffe.png)

Alternative routing API for Giraffe web applications which is aimed at maximum performance.

[![NuGet Info](https://buildstats.info/nuget/Giraffe.TokenRouter?includePreReleases=true)](https://www.nuget.org/packages/Giraffe.TokenRouter/)

| Windows | Linux |
| :------ | :---- |
| [![Windows Build status](https://ci.appveyor.com/api/projects/status/914030ec0lrc0vti/branch/develop?svg=true)](https://ci.appveyor.com/project/dustinmoris/giraffe-tokenrouter/branch/develop) | [![Linux Build status](https://travis-ci.org/giraffe-fsharp/Giraffe.TokenRouter.svg?branch=develop)](https://travis-ci.org/giraffe-fsharp/Giraffe.TokenRouter/builds?branch=develop) |
| [![Windows Build history](https://buildstats.info/appveyor/chart/dustinmoris/giraffe-tokenrouter?branch=develop&includeBuildsFromPullRequest=false)](https://ci.appveyor.com/project/dustinmoris/giraffe-tokenrouter/history?branch=develop) | [![Linux Build history](https://buildstats.info/travisci/chart/giraffe-fsharp/Giraffe.TokenRouter?branch=develop&includeBuildsFromPullRequest=false)](https://travis-ci.org/giraffe-fsharp/Giraffe.TokenRouter/builds?branch=develop) |

## Table of contents

- [Documentation](#documentation)
- [Nightly builds and NuGet feed](#nightly-builds-and-nuget-feed)
- [More information](#more-information)
- [License](#license)

## Documentation

The `Giraffe.TokenRouter` module adds alternative `HttpHandler` functions to route incoming HTTP requests through a basic [Radix Tree](https://en.wikipedia.org/wiki/Radix_tree). Several routing handlers (e.g.: `routef` and `subRoute`) have been overridden in such a way that path matching and value parsing are significantly faster than using the basic `choose` function.

This implementation assumes that additional memory and compilation time is not an issue. If speed and performance of parsing and path matching is required then the `Giraffe.TokenRouter` is the preferred option.

### router

The base of all routing decisions is a `router` function instead of the default `choose` function when using the `Giraffe.TokenRouter` module.

The `router` HttpHandler takes two arguments, a `HttpHandler` to execute when no route can be matched (typical 404 Not Found handler) and secondly a list of all routing functions.

#### Example:

Defining a basic router and routes

```fsharp
let notFound = setStatusCode 404 >=> text "Not found"
let app =
    router notFound [
        route "/"       (text "index")
        route "/about"  (text "about")
    ]
```

### routing functions

When using the `Giraffe.TokenRouter` module the main routing functions have been slightly overridden to match the alternative (speed improved) implementation.

The `route` and `routef` handlers work the exact same way as before, except that the continuation handler needs to be enclosed in parentheses or captured by the `<|` or `=>` operators.

The http handlers `GET`, `POST`, `PUT` and `DELETE` are functions which take a list of nested http handler functions similar to before.

The `subRoute` handler has been altered in order to accept an additional parameter of child routing functions. All child routing functions will presume that the given sub path has been prepended.

#### Example:

Defining a basic router and routes

```fsharp
let notFound = setStatusCode 404 >=> text "Not found"
let app =
    router notFound [
        route "/"       (text "index")
        route "/about"  (text "about")
        routef "parsing/%s/%i" (fun (s,i) -> text (sprintf "Recieved %s & %i" s i))
        subRoute "/api" [
            GET [
                route "/"       (text "api index")
                route "/about"  (text "api about")
                subRoute "/v2" [
                    route "/"       (text "api v2 index")
                    route "/about"  (text "api v2 about")
                ]
            ]

        ]
    ]
```

## Nightly builds and NuGet feed

All official Giraffe packages are published to the official and public NuGet feed.

Unofficial builds (such as pre-release builds from the `develop` branch and pull requests) produce unofficial pre-release NuGet packages which can be pulled from the project's public NuGet feed on AppVeyor:

```
https://ci.appveyor.com/nuget/giraffe-tokenrouter
```

If you add this source to your NuGet CLI or project settings then you can pull unofficial NuGet packages for quick feature testing or urgent hot fixes.

## More information

For more information about Giraffe, how to set up a development environment, contribution guidelines and more please visit the [main documentation](https://github.com/giraffe-fsharp/Giraffe/blob/master/DOCUMENTATION.md) page.

## License

[Apache 2.0](https://raw.githubusercontent.com/giraffe-fsharp/Giraffe.TokenRouter/master/LICENSE)