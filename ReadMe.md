To start gRPC service
```
cd GrpcService/
dotnet run -c retail -- -w gRPC
```

To start Dotnet Http2 service
```
cd GrpcService/
dotnet run -c retail -- -w http2
```

To start JAVA reactor Http2, Tcp (a.k.a. Rntbd) service
```
cd server-java/
mvn clean package -f pom.xml -DskipTests -Dgpg.skip -Ppackage-assembly
java -jar ./target/TestServer-1.0-SNAPSHOT-jar-with-dependencies.jar
```

How to run the client's
```
cd Client/
dotnet run -c retail -- -w [Http11|DotnetHttp2|ReactorHttp2|Grpc|Tcp]  -e [localhost/ipv4] -c 8 --mcpe 2
```
