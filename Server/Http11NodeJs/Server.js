var express = require('express');
var app = express();

app.get('/dbs/:dbId/cols/:colId', function (req, res) {
    // console.log("Serving request %s:%s",req.params.dbId, req.params.colId);

    req.pipe(res);
 });

app.post('/dbs/:dbId/cols/:colId', function (req, res) {
    // console.log("Serving request %s:%s",req.params.dbId, req.params.colId);

    req.pipe(res);
 });

app.listen(8000);
