<h1>15K RPS per Core Challenge</h1>

<h3>Proposal</h3>

![image](https://user-images.githubusercontent.com/6880899/172974154-57e81c2a-80d3-4e0c-8fa7-c1091fbc116d.png)


# Tasks

## With pre-configured account routing info
- [ ] Http1 and Http2 E2E integration 
- [ ] Rntbd2 E2E integration (Use .NET Pipe abstraction if possible leverage on client as well)
- [ ] Grpc implementation
- [ ] Http3 endpoint and E2E integration 
- [ ] Http2 Comsos header HPAC possibility (.NET seems only supporting for default header's)
- [ ] Native Rntbd server (With Backend code)
- [ ] Native Http2 server (explore Http2 implementations)

## Benchmarking (Local core)
- [ ] Server AI integration (RPS, latency and scenario labeling/dimension)
- [ ] Azure stand-by runner (Router and proxy on same node)
- [ ] Azure stand-by runner (Router and proxy on different node's)

## E2E scenario with auto account routing
- [ ] Dotnet implementation 



## E2E scenario flow (including session concept)
- [ ] Dotnet implementation 


## Few misc
- [ ] Rntbd full length including headers & body
- [ ] Rntbd headers order (front load routing context header's)
- [ ] Message protocol with routing context explictly
- [ ] Avoid replica address rewriting (make it full padd-through)

## Possible optimizatons for Rntbd 
- [ ] Ordering of Token through templates (HACK right solution)
- [ ] Avoid explict loop for all required token presence (PreCount is one possible solution)
- [ ] Avoid explict loop for content length (Can it be in-flight compute on every token set)?

## Possible extensions
- [ ] Rntbd test server (CTL altenative)


To start service
```
cd Service/
dotnet run -c retail 
```

How to run the client's
```
cd Client/
dotnet run -c retail -- -w [DotnetHttp1|DotnetHttp2|DotnetRntbd2]  -c 100 -m 2
```
'-m': Max connections per endpoint
'-c': Concurrency of worload
