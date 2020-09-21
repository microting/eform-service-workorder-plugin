#!/bin/bash

cd ~

if [ -d "Documents/workspace/microting/eform-debian-service/Plugins/ServiceWorkOrdersPlugin" ]; then
	rm -fR Documents/workspace/microting/eform-debian-service/Plugins/ServiceWorkOrdersPlugin
fi

mkdir Documents/workspace/microting/eform-debian-service/Plugins

cp -av Documents/workspace/microting/eform-service-workorder-plugin/ServiceWorkOrdersPlugin Documents/workspace/microting/eform-debian-service/Plugins/ServiceWorkOrdersPlugin
