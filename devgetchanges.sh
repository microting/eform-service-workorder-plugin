#!/bin/bash

cd ~

if [ -d "Documents/workspace/microting/eform-service-workorder-plugin/ServiceWorkOrdersPlugin" ]; then
	rm -fR Documents/workspace/microting/eform-service-workorder-plugin/ServiceWorkOrdersPlugin
fi

cp -av Documents/workspace/microting/eform-debian-service/Plugins/ServiceWorkOrdersPlugin Documents/workspace/microting/eform-service-workorder-plugin/ServiceWorkOrdersPlugin
