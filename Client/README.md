
For HTTP1: 
```
dotnet run CosmosBenchmark.csproj -w Echo11Server -c 10 -m 10
```

For HTTP2: 
```
dotnet run CosmosBenchmark.csproj -w Echo20Server -e https://localhost:8081 --database db1 --container c1 -n 10000 --pl 10
```

For Rntbd2
```
dotnet run CosmosBenchmark.csproj -c Release -- -w DotNetRntbd2 -c 10 -m 10
```
