Start server
```
cd Server
dotnet run -c Release
```


Client benchmarking


For HTTP1: 
```
cd Client
dotnet run -c Release -- -w DotnetHttp1 -c 100 -m 100
```

For HTTP2: 
```
cd Client
dotnet run -c Release -- -w DotnetHttp2 -c 100 -m 4
```

For Rntbd2: 
```
cd Client
dotnet run -c Release -- -w DotnetRntbd2 -c 100 -m 4
```

