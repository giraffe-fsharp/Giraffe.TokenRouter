# Giraffe.TokenRouter

![Giraffe](https://raw.githubusercontent.com/giraffe-fsharp/Giraffe/master/giraffe.png)

Alternative routing API for Giraffe web applications which is aimed at maximum performance.

[![NuGet Info](https://buildstats.info/nuget/Giraffe.TokenRouter?includePreReleases=true)](https://www.nuget.org/packages/Giraffe.TokenRouter/)

[![Build and test](https://github.com/giraffe-fsharp/Giraffe.TokenRouter/actions/workflows/build.yml/badge.svg?branch=master)](https://github.com/giraffe-fsharp/Giraffe.TokenRouter/actions/workflows/build.yml)

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

### NuGet trusted publishing

GitHub releases publish through [build.yml](.github/workflows/build.yml) using
GitHub OIDC and `NuGet/login` as the personal NuGet account `dsyme`, with package
permissions provided by the `fsprojects` NuGet organization.

Before publishing, configure a [NuGet trusted publishing policy](https://www.nuget.org/account/trustedpublishing)
with package owner `fsprojects`, GitHub repository owner `giraffe-fsharp`,
repository `Giraffe.TokenRouter`, workflow filename `build.yml`, no environment,
and scope to push new versions of the existing package `Giraffe.TokenRouter`.
Keep `dsyme` an active member of the NuGet organization.

After a successful trusted-publishing release, remove the obsolete GitHub
`NUGET_KEY` secret and revoke its NuGet API key if it is not used elsewhere.

### Package feeds

All official Giraffe packages are published to the official and public NuGet feed.

`master` is the integration branch; open pull requests against `master`.
GitHub Actions builds and tests pull requests and pushes to `master` on Linux,
Windows and macOS. Successful pushes to `master` also publish unofficial
pre-release packages to the [project's NuGet feed on GitHub](https://github.com/orgs/giraffe-fsharp/packages).
Pull requests never publish packages. Official nuget.org publication remains
triggered only by a published GitHub release.

Unofficial packages use the version `0.0.0-ci.<run-id>`. Pull-request packages
can be downloaded from the `nupkg` artifact on the GitHub Actions run.

After merging the master-only migration, maintainers can remove the obsolete
`develop` branch and its branch protection rule. Check that it still has no
commits missing from `master` before deleting it.

## More information

For more information about Giraffe, how to set up a development environment, contribution guidelines and more please visit the [main documentation](https://github.com/giraffe-fsharp/Giraffe/blob/master/DOCUMENTATION.md) page.

## License

[Apache 2.0](https://raw.githubusercontent.com/giraffe-fsharp/Giraffe.TokenRouter/master/LICENSE)